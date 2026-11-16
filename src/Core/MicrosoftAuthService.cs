using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace LiteLauncher.Core
{
    /// <summary>
    /// 1.7.0（F-1702）：微软正版登录（可选，测试版）。
    /// 实现 OAuth 2.0 设备码流程 → Xbox Live 认证 → XSTS 授权 → Minecraft Services 登录
    /// → 档案校验 → 刷新令牌 的完整协议链路。所有端点均为官方 HTTPS 地址并加入
    /// HttpUtil 白名单；用户必须在设置页配置自己注册的 Azure 应用 client_id 才能启用。
    /// 约定（PRD 7.4）：未跑通完整链路前，界面不得展示「微软登录成功」——
    /// 本服务在任一步失败时只报告具体错误，由调用方显示为失败状态。
    /// 消息构造/解析为纯静态函数，便于离线单测验证协议与错误映射。
    /// </summary>
    public sealed class MicrosoftAuthService
    {
        public static string DeviceCodeUrl =
            "https://login.microsoftonline.com/consumers/oauth2/v2.0/devicecode";
        public static string TokenUrl =
            "https://login.microsoftonline.com/consumers/oauth2/v2.0/token";
        public static string XboxAuthUrl =
            "https://user.auth.xboxlive.com/user/authenticate";
        public static string XstsUrl =
            "https://xsts.auth.xboxlive.com/xsts/authorize";
        public static string MinecraftLoginUrl =
            "https://api.minecraftservices.com/authentication/login_with_xbox";
        public static string MinecraftProfileUrl =
            "https://api.minecraftservices.com/minecraft/profile";
        /// <summary>需要 offline_access 以换取可长期刷新的 refresh_token。</summary>
        public static string Scope = "XboxLive.signin offline_access";
        public static string XstsRelyingParty = "rp://api.minecraftservices.com/";

        /// <summary>设备码请求返回。</summary>
        public sealed class DeviceCodeResult
        {
            public string DeviceCode;
            public string UserCode;
            public string VerificationUri;
            public int IntervalSeconds;
            public int ExpiresInSeconds;
        }

        /// <summary>令牌轮询结果。</summary>
        public sealed class TokenPollResult
        {
            public bool Complete;
            /// <summary>pending = 等待用户授权；expired / denied / 其他失败时 false。</summary>
            public bool Pending;
            public string ErrorCode;
            public string ErrorDescription;
            public string AccessToken;
            public string RefreshToken;
            public long ExpiresInSeconds;
        }

        /// <summary>完整登录后的 Minecraft 账号信息。</summary>
        public sealed class MicrosoftProfile
        {
            public string Uuid;
            public string Name;
            public string AccessToken;
            public string RefreshToken;
            public long ExpiresAtUnix;
        }

        // ---------- 请求构造 ----------

        public static string BuildDeviceCodeBody(string clientId)
        {
            return "client_id=" + Uri.EscapeDataString(clientId) +
                   "&scope=" + Uri.EscapeDataString(Scope);
        }

        public static string BuildTokenBody(string clientId, string deviceCode)
        {
            return "client_id=" + Uri.EscapeDataString(clientId) +
                   "&grant_type=urn:ietf:params:oauth:grant-type:device_code" +
                   "&device_code=" + Uri.EscapeDataString(deviceCode) +
                   "&scope=" + Uri.EscapeDataString(Scope);
        }

        public static string BuildRefreshBody(string clientId, string refreshToken)
        {
            return "client_id=" + Uri.EscapeDataString(clientId) +
                   "&grant_type=refresh_token" +
                   "&refresh_token=" + Uri.EscapeDataString(refreshToken) +
                   "&scope=" + Uri.EscapeDataString(Scope);
        }

        public static string BuildXblBody(string accessToken)
        {
            return JsonUtil.Serialize(new Dictionary<string, object>
            {
                { "Properties", new Dictionary<string, object>
                    {
                        { "AuthMethod", "RPS" },
                        { "SiteName", "user.auth.xboxlive.com" },
                        { "RpsTicket", "d=" + accessToken }
                    }
                },
                { "RelyingParty", "http://auth.xboxlive.com" },
                { "TokenType", "JWT" }
            });
        }

        public static string BuildXstsBody(string xblToken)
        {
            return JsonUtil.Serialize(new Dictionary<string, object>
            {
                { "Properties", new Dictionary<string, object>
                    {
                        { "SandboxId", "RETAIL" },
                        { "UserTokens", new List<string> { xblToken } }
                    }
                },
                { "RelyingParty", XstsRelyingParty },
                { "TokenType", "JWT" }
            });
        }

        /// <summary>Minecraft 登录请求体。userHash 取自 XSTS 响应的 DisplayClaims.xui[0].uhs。</summary>
        public static string BuildMcLoginBody(string userHash, string xstsToken)
        {
            return JsonUtil.Serialize(new Dictionary<string, object>
            {
                { "identityToken", "XBL3.0 x=" + userHash + ";" + xstsToken }
            });
        }

        // ---------- 响应解析 ----------

        public static DeviceCodeResult ParseDeviceCode(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                throw new InvalidDataException("设备码响应为空。");
            }
            IDictionary<string, object> root = JsonUtil.ParseObject(json);
            DeviceCodeResult result = new DeviceCodeResult();
            result.DeviceCode = JsonUtil.GetString(root, "device_code");
            result.UserCode = JsonUtil.GetString(root, "user_code");
            result.VerificationUri = JsonUtil.GetString(root, "verification_uri");
            result.IntervalSeconds = JsonUtil.GetLong(root, "interval", 0) > 0
                ? (int)JsonUtil.GetLong(root, "interval", 0) : 5;
            result.ExpiresInSeconds = (int)JsonUtil.GetLong(root, "expires_in", 900);
            if (string.IsNullOrEmpty(result.DeviceCode) || string.IsNullOrEmpty(result.UserCode))
            {
                throw new InvalidDataException("设备码响应缺少关键字段（可能是 client_id 无效或被拒）。");
            }
            return result;
        }

        /// <summary>
        /// 解析令牌轮询响应。2xx 且含 access_token 视为完成；
        /// 403 + error=authorization_pending 视为等待中；其余错误带 error/error_description。
        /// </summary>
        public static TokenPollResult ParseTokenResponse(int code, string body)
        {
            TokenPollResult result = new TokenPollResult();
            if (string.IsNullOrEmpty(body))
            {
                result.ErrorCode = "empty_response";
                result.ErrorDescription = "令牌端点返回空响应。";
                return result;
            }
            IDictionary<string, object> root = JsonUtil.ParseObject(body);
            if (code >= 200 && code < 300 &&
                !string.IsNullOrEmpty(JsonUtil.GetString(root, "access_token")))
            {
                result.Complete = true;
                result.AccessToken = JsonUtil.GetString(root, "access_token");
                result.RefreshToken = JsonUtil.GetString(root, "refresh_token");
                result.ExpiresInSeconds = JsonUtil.GetLong(root, "expires_in", 0);
                return result;
            }
            string error = JsonUtil.GetString(root, "error");
            if (string.Equals(error, "authorization_pending", StringComparison.OrdinalIgnoreCase))
            {
                result.Pending = true;
                return result;
            }
            result.ErrorCode = string.IsNullOrEmpty(error) ? "http_" + code : error;
            result.ErrorDescription = JsonUtil.GetString(root, "error_description");
            if (string.IsNullOrEmpty(result.ErrorDescription))
            {
                result.ErrorDescription = "令牌端点返回 HTTP " + code;
            }
            return result;
        }

        /// <summary>从 XBL 响应取 token。</summary>
        public static string ParseXblToken(string json)
        {
            return ParseBearer(json);
        }

        /// <summary>从 XSTS 响应取 token 与 userHash（DisplayClaims.xui[0].uhs）。</summary>
        public static string ParseXstsToken(string json, out string userHash)
        {
            userHash = null;
            if (string.IsNullOrEmpty(json))
            {
                throw new InvalidDataException("XSTS 响应为空。");
            }
            IDictionary<string, object> root = JsonUtil.ParseObject(json);
            // DisplayClaims 是对象（xui 数组在对象内），先取对象再取 xui。
            object claimsValue;
            IDictionary<string, object> claims = JsonUtil.TryGet(root, "DisplayClaims", out claimsValue)
                ? JsonUtil.AsObject(claimsValue) : null;
            System.Collections.IList xuiList = JsonUtil.GetArray(claims, "xui");
            if (xuiList != null && xuiList.Count > 0)
            {
                IDictionary<string, object> xui = JsonUtil.AsObject(xuiList[0]);
                if (xui != null)
                {
                    userHash = JsonUtil.GetString(xui, "uhs");
                }
            }
            return ParseBearer(json);
        }

        /// <summary>从 Minecraft 登录响应取 access_token。</summary>
        public static string ParseMcLogin(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                throw new InvalidDataException("Minecraft 登录响应为空。");
            }
            IDictionary<string, object> root = JsonUtil.ParseObject(json);
            string token = JsonUtil.GetString(root, "access_token");
            if (string.IsNullOrEmpty(token))
            {
                throw new InvalidDataException("Minecraft 登录响应缺少 access_token。");
            }
            return token;
        }

        /// <summary>解析档案响应为 UUID / 名称。</summary>
        public static void ParseProfile(string json, out string uuid, out string name)
        {
            uuid = null;
            name = null;
            if (string.IsNullOrEmpty(json))
            {
                return;
            }
            IDictionary<string, object> root = JsonUtil.ParseObject(json);
            uuid = JsonUtil.GetString(root, "id");
            name = JsonUtil.GetString(root, "name");
        }

        private static string ParseBearer(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                throw new InvalidDataException("认证响应为空。");
            }
            IDictionary<string, object> root = JsonUtil.ParseObject(json);
            string token = JsonUtil.GetString(root, "Token");
            if (string.IsNullOrEmpty(token))
            {
                throw new InvalidDataException("认证响应缺少 Token。");
            }
            return token;
        }

        /// <summary>
        /// 把 XSTS 错误（HTTP 401 + XErr 码）映射为对用户可读的原因。
        /// 覆盖常见情况；未知码返回原始信息。
        /// </summary>
        public static string MapXstsError(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                return "XSTS 授权失败。";
            }
            try
            {
                IDictionary<string, object> root = JsonUtil.ParseObject(json);
                long xerr = 0;
                object raw;
                if (!JsonUtil.TryGet(root, "XErr", out raw))
                {
                    raw = null;
                }
                if (raw != null)
                {
                    if (raw is IConvertible)
                    {
                        try
                        {
                            xerr = Convert.ToInt64(raw, System.Globalization.CultureInfo.InvariantCulture);
                        }
                        catch
                        {
                        }
                    }
                    else
                    {
                        long.TryParse(Convert.ToString(raw), out xerr);
                    }
                }
                if (xerr == 2148916233 || xerr == 2148916235)
                {
                    return "该微软账号没有 Minecraft：Java 版。";
                }
                if (xerr == 2148916236 || xerr == 2148916237)
                {
                    return "该账号受家庭限制，无法登录（XSTS 2148916236/37）。";
                }
                if (xerr == 2148916229)
                {
                    return "请检查微软账号是否正常（可能被限制）。";
                }
                if (xerr == 2148916225)
                {
                    return "登录凭据无效，请重新授权。";
                }
                if (xerr != 0)
                {
                    return "XSTS 拒绝授权（XErr " + xerr + "）。";
                }
            }
            catch
            {
            }
            return "XSTS 授权失败。";
        }

        /// <summary>
        /// 完整流程（异步）：设备码 → 轮询令牌 → Xbox → XSTS → Minecraft → 档案。
        /// 任一步失败抛异常；调用方负责显示失败状态（不得显示「登录成功」）。
        /// </summary>
        /// <summary>轮询期间用于展示的间隔。</summary>
        public static int EffectiveInterval(DeviceCodeResult device)
        {
            return Math.Max(2, Math.Min(device.IntervalSeconds, 10));
        }

        public async Task<DeviceCodeResult> RequestDeviceCodeAsync(
            string clientId, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(clientId))
            {
                throw new InvalidOperationException("未配置微软应用 client_id。");
            }
            HttpUtil.HttpStatus status = await HttpUtil.PostFormAsync(
                DeviceCodeUrl, BuildDeviceCodeBody(clientId.Trim()), 16384,
                cancellationToken).ConfigureAwait(false);
            if (status.Code != 200)
            {
                throw new InvalidOperationException(
                    "设备码请求失败（HTTP " + status.Code + "）：" + Truncate(status.Body));
            }
            return ParseDeviceCode(status.Body);
        }

        /// <summary>
        /// 轮询令牌端点直到完成 / 过期 / 被拒绝 / 用户取消。
        /// 返回完整轮询结果（含 access_token 与 refresh_token）。
        ///
        /// 1.7.5 修复（B-07）：1.7.0 的实现返回 string（只有 access_token），
        /// 已解析出的 RefreshToken 被丢弃，调用方只能传 null 给 CompleteExchangeAsync，
        /// refresh_token 因此永远落盘为空、无法刷新。改为返回 TokenPollResult。
        /// </summary>
        public async Task<TokenPollResult> PollTokenAsync(
            string clientId, string deviceCode, int intervalSeconds, int expiresInSeconds,
            CancellationToken cancellationToken)
        {
            long deadline = DateTime.UtcNow.Ticks / TimeSpan.TicksPerSecond + expiresInSeconds;
            while (true)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    throw new OperationCanceledException("用户取消了微软登录。");
                }
                HttpUtil.HttpStatus status = await HttpUtil.PostFormAsync(
                    TokenUrl, BuildTokenBody(clientId, deviceCode), 16384,
                    cancellationToken).ConfigureAwait(false);
                TokenPollResult poll = ParseTokenResponse(status.Code, status.Body);
                if (poll.Complete)
                {
                    return poll;
                }
                if (poll.Pending)
                {
                    if (DateTime.UtcNow.Ticks / TimeSpan.TicksPerSecond >= deadline)
                    {
                        throw new InvalidOperationException("授权码已过期，请重新开始登录。");
                    }
                    int wait = Math.Max(2, Math.Min(intervalSeconds, 10));
                    await Task.Delay(TimeSpan.FromSeconds(wait), cancellationToken)
                        .ConfigureAwait(false);
                    continue;
                }
                throw new InvalidOperationException(
                    "授权被拒绝（" + poll.ErrorCode + "）：" + poll.ErrorDescription);
            }
        }

        /// <summary>用 access_token 走 Xbox → XSTS → Minecraft 登录，并校验档案。</summary>
        public async Task<MicrosoftProfile> CompleteExchangeAsync(
            string clientId, string oauthAccessToken, string oauthRefreshToken,
            CancellationToken cancellationToken)
        {
            // 1) XBL
            string xblJson = await HttpUtil.PostJsonStringAsync(
                XboxAuthUrl, BuildXblBody(oauthAccessToken), 16384,
                cancellationToken).ConfigureAwait(false);
            string xblToken = ParseXblToken(xblJson);

            // 2) XSTS
            string xstsJson = await HttpUtil.PostJsonStringAsync(
                XstsUrl, BuildXstsBody(xblToken), 16384,
                cancellationToken).ConfigureAwait(false);
            string userHash;
            string xstsToken = ParseXstsToken(xstsJson, out userHash);
            if (string.IsNullOrEmpty(userHash))
            {
                throw new InvalidDataException("XSTS 响应缺少 uhs。");
            }

            // 3) Minecraft services
            string mcJson = await HttpUtil.PostJsonStringAsync(
                MinecraftLoginUrl, BuildMcLoginBody(userHash, xstsToken), 16384,
                cancellationToken).ConfigureAwait(false);
            string mcToken = ParseMcLogin(mcJson);

            // 4) 档案校验：没有 Minecraft 档案则视为失败（不得显示成功）。
            HttpUtil.HttpStatus profileStatus = await HttpUtil.GetStatusAsync(
                MinecraftProfileUrl + "?cache=" + Guid.NewGuid().ToString("N"),
                mcToken, 16384, cancellationToken).ConfigureAwait(false);
            if (profileStatus.Code == 404)
            {
                throw new InvalidOperationException("该微软账号没有 Minecraft：Java 版档案。");
            }
            if (profileStatus.Code != 200)
            {
                throw new InvalidOperationException(
                    "档案校验失败（HTTP " + profileStatus.Code + "）。");
            }
            string uuid;
            string name;
            ParseProfile(profileStatus.Body, out uuid, out name);
            if (string.IsNullOrEmpty(uuid) || string.IsNullOrEmpty(name))
            {
                throw new InvalidDataException("档案响应缺少 id/name。");
            }
            MicrosoftProfile profile = new MicrosoftProfile();
            profile.Uuid = uuid;
            profile.Name = name;
            profile.AccessToken = mcToken;
            profile.RefreshToken = oauthRefreshToken;
            profile.ExpiresAtUnix = 0;
            return profile;
        }

        /// <summary>OAuth 令牌刷新结果。</summary>
        public sealed class OAuthRefreshResult
        {
            public bool Success;
            public string AccessToken;
            public string RefreshToken;
        }

        /// <summary>
        /// 用 refresh_token 刷新 OAuth 令牌；成功后返回新的 access_token + refresh_token
        /// （微软刷新会旋转 refresh_token）。失败返回 Success=false，由调用方提示重新登录。
        /// </summary>
        public async Task<OAuthRefreshResult> TryRefreshOAuthAsync(
            string clientId, string refreshToken, CancellationToken cancellationToken)
        {
            OAuthRefreshResult result = new OAuthRefreshResult();
            if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrEmpty(refreshToken))
            {
                return result;
            }
            try
            {
                HttpUtil.HttpStatus status = await HttpUtil.PostFormAsync(
                    TokenUrl, BuildRefreshBody(clientId.Trim(), refreshToken), 16384,
                    cancellationToken).ConfigureAwait(false);
                TokenPollResult poll = ParseTokenResponse(status.Code, status.Body);
                if (!poll.Complete)
                {
                    return result;
                }
                result.Success = true;
                result.AccessToken = poll.AccessToken;
                result.RefreshToken = poll.RefreshToken;
                return result;
            }
            catch (Exception ex)
            {
                Log.Warn("微软令牌刷新失败：" + ex.Message);
                return result;
            }
        }

        private static string Truncate(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "";
            }
            return text.Length <= 120 ? text : text.Substring(0, 120);
        }
    }
}
