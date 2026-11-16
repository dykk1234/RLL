using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LiteLauncher.Core
{
    /// <summary>yggdrasil 认证成功后的会话数据。</summary>
    public sealed class YggdrasilResult
    {
        public string AccessToken;
        public string ClientToken;
        public string Uuid;
        public string Name;
        /// <summary>服务器返回的错误描述（认证失败时）。</summary>
        public string ErrorMessage;
        public bool Success;
    }

    /// <summary>
    /// 第三方认证（yggdrasil / authlib-injector）协议的客户端。
    /// 只与用户主动添加的认证服务器通信；所有请求仍走 HttpUtil 的
    /// 白名单 + HTTPS + 重定向复检约束。
    /// </summary>
    public static class YggdrasilClient
    {
        public const int MaxResponseBytes = 4 * 1024 * 1024;
        private const string AuthenticatePath = "/authserver/authenticate";
        private const string ValidatePath = "/authserver/validate";
        private const string InvalidatePath = "/authserver/invalidate";

        public static string AuthenticateEndpoint(string serverUrl)
        {
            return AuthValidation.NormalizeAuthServerUrl(serverUrl) + AuthenticatePath;
        }

        public static string ValidateEndpoint(string serverUrl)
        {
            return AuthValidation.NormalizeAuthServerUrl(serverUrl) + ValidatePath;
        }

        /// <summary>生成客户端令牌：用于把多次认证绑定到同一客户端。</summary>
        public static string GenerateClientToken()
        {
            return Guid.NewGuid().ToString("D");
        }

        /// <summary>
        /// 纯解析：把 /authserver/authenticate 的响应 JSON 转成会话数据。
        /// 抽出为纯函数便于离线测试。
        /// </summary>
        public static YggdrasilResult ParseAuthenticateResponse(string json)
        {
            YggdrasilResult result = new YggdrasilResult();
            if (string.IsNullOrEmpty(json))
            {
                result.ErrorMessage = "认证服务器返回了空响应。";
                return result;
            }
            try
            {
                Dictionary<string, object> root = JsonUtil.ParseObject(json);
                string error = JsonUtil.GetString(root, "error");
                if (!string.IsNullOrEmpty(error))
                {
                    string errorMessage = JsonUtil.GetString(root, "errorMessage");
                    result.ErrorMessage = string.IsNullOrEmpty(errorMessage) ? error : errorMessage;
                    return result;
                }

                string accessToken = JsonUtil.GetString(root, "accessToken");
                string clientToken = JsonUtil.GetString(root, "clientToken");
                if (string.IsNullOrEmpty(accessToken))
                {
                    result.ErrorMessage = "认证响应缺少 accessToken。";
                    return result;
                }

                IDictionary<string, object> profile = JsonUtil.GetObject(root, "selectedProfile");
                if (profile == null)
                {
                    result.ErrorMessage = "认证响应缺少 selectedProfile（该账号可能没有任何可用档案）。";
                    return result;
                }
                string uuid = JsonUtil.GetString(profile, "id");
                string name = JsonUtil.GetString(profile, "name");
                // 规范化为标准带连字符形式：有的服务器（如 LittleSkin 的
                // Yggdrasil Connect）返回 32 位无连字符，直接用会被判成格式无效。
                uuid = AuthValidation.NormalizeUuid(uuid);
                if (uuid.Length == 0)
                {
                    result.ErrorMessage = "认证服务器返回的 UUID 格式无效。";
                    return result;
                }
                if (!AuthValidation.IsValidAuthDisplayName(name))
                {
                    result.ErrorMessage = "认证服务器返回的档案名称无效。";
                    return result;
                }

                result.AccessToken = accessToken;
                result.ClientToken = clientToken;
                result.Uuid = uuid;
                result.Name = name;
                result.Success = true;
                return result;
            }
            catch (Exception ex)
            {
                result.ErrorMessage = "无法解析认证响应：" + ex.Message;
                return result;
            }
        }

        /// <summary>
        /// 向认证服务器发起登录。密码只在本方法的请求体中存在，
        /// 调用方负责在成功后立即丢弃（不写入任何持久化存储）。
        /// </summary>
        public static async Task<YggdrasilResult> AuthenticateAsync(
            string serverUrl,
            string username,
            string password,
            string clientToken,
            CancellationToken cancellationToken)
        {
            string normalized = AuthValidation.NormalizeAuthServerUrl(serverUrl);
            if (!AuthValidation.IsValidAuthServerUrl(normalized))
            {
                YggdrasilResult invalid = new YggdrasilResult();
                invalid.ErrorMessage = "认证服务器地址无效：必须是 https:// 开头的合法地址。";
                return invalid;
            }
            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                YggdrasilResult invalid = new YggdrasilResult();
                invalid.ErrorMessage = "账号与密码不能为空。";
                return invalid;
            }

            string body = "{\"agent\":{\"name\":\"Minecraft\",\"version\":1}," +
                          "\"username\":\"" + JsonEscape(username) + "\"," +
                          "\"password\":\"" + JsonEscape(password) + "\"," +
                          "\"clientToken\":\"" + JsonEscape(clientToken ?? "") + "\"," +
                          "\"requestUser\":false}";
            string endpoint = normalized + AuthenticatePath;
            try
            {
                HttpUtil.HttpStatus status = await HttpUtil.PostJsonAsync(
                    endpoint, body, MaxResponseBytes, cancellationToken).ConfigureAwait(false);
                YggdrasilResult result = ParseAuthenticateResponse(status.Body);
                if (result.Success)
                {
                    Log.Info("第三方认证成功：" + result.Name + " @" + AuthValidation.GetHost(normalized));
                }
                else if (string.IsNullOrEmpty(result.ErrorMessage) &&
                         (status.Code < 200 || status.Code >= 300))
                {
                    result.ErrorMessage = "认证服务器返回 HTTP " + status.Code +
                                          (string.IsNullOrEmpty(status.ReasonPhrase)
                                              ? ""
                                              : "（" + status.ReasonPhrase + "）");
                }
                else if (result.Success == false)
                {
                    Log.Warn("第三方认证被拒绝：" + (result.ErrorMessage ?? "未知错误"));
                }
                return result;
            }
            catch (Exception ex)
            {
                // 服务器返回 4xx 时（如 403 账号密码错误）异常消息里带着响应体，
                // 里面其实是标准的 yggdrasil 业务错误（errorMessage）。
                // 旧实现直接包成"无法连接认证服务器"，把"账号密码错误"误报成网络问题，
                // 用户会一直以为是连不上服务器。
                YggdrasilResult failed = new YggdrasilResult();
                string friendly = ExtractServerError(ex.Message);
                if (!string.IsNullOrEmpty(friendly))
                {
                    failed.ErrorMessage = friendly;
                }
                else
                {
                    failed.ErrorMessage = "无法连接认证服务器：" + ex.Message;
                }
                return failed;
            }
        }

        /// <summary>
        /// 从异常消息里提取服务器给出的业务错误描述。
        /// HttpUtil 抛出的消息形如：HTTP 403 (Forbidden)：{"error":"...","errorMessage":"..."}
        /// 若能在其中找到 errorMessage，就直接显示它，而不是笼统的网络错误。
        /// </summary>
        public static string ExtractServerError(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return "";
            }
            int brace = message.IndexOf('{');
            if (brace < 0)
            {
                return "";
            }
            string json = message.Substring(brace);
            try
            {
                Dictionary<string, object> root = JsonUtil.ParseObject(json);
                string errorMessage = JsonUtil.GetString(root, "errorMessage");
                if (!string.IsNullOrEmpty(errorMessage))
                {
                    return errorMessage;
                }
                string error = JsonUtil.GetString(root, "error");
                if (!string.IsNullOrEmpty(error))
                {
                    return error;
                }
            }
            catch
            {
            }
            return "";
        }

        /// <summary>
        /// 校验已保存的会话令牌是否仍然有效（POST /authserver/validate）。
        /// 返回 true 表示有效；false 表示失效或服务器不可用（调用方需区分提示）。
        /// </summary>
        public static async Task<bool> ValidateAsync(
            string serverUrl,
            string accessToken,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(accessToken))
            {
                return false;
            }
            string normalized = AuthValidation.NormalizeAuthServerUrl(serverUrl);
            if (!AuthValidation.IsValidAuthServerUrl(normalized))
            {
                return false;
            }
            string body = "{\"accessToken\":\"" + JsonEscape(accessToken) + "\"}";
            string endpoint = normalized + ValidatePath;
            try
            {
                HttpUtil.HttpStatus status = await HttpUtil.PostJsonAsync(
                    endpoint, body, MaxResponseBytes, cancellationToken).ConfigureAwait(false);
                return status.Code >= 200 && status.Code < 300;
            }
            catch (Exception ex)
            {
                Log.Warn("校验会话令牌失败：" + ex.Message);
                return false;
            }
        }

        /// <summary>使会话令牌失效（登出）。失败不抛出，仅记录日志。</summary>
        public static async Task InvalidateAsync(
            string serverUrl,
            string accessToken,
            string clientToken,
            CancellationToken cancellationToken)
        {
            try
            {
                string normalized = AuthValidation.NormalizeAuthServerUrl(serverUrl);
                if (!AuthValidation.IsValidAuthServerUrl(normalized) ||
                    string.IsNullOrEmpty(accessToken))
                {
                    return;
                }
                string body = "{\"accessToken\":\"" + JsonEscape(accessToken) + "\"," +
                              "\"clientToken\":\"" + JsonEscape(clientToken ?? "") + "\"}";
                string endpoint = normalized + InvalidatePath;
                HttpUtil.HttpStatus status = await HttpUtil.PostJsonAsync(
                    endpoint, body, MaxResponseBytes, cancellationToken).ConfigureAwait(false);
                Log.Info("已注销第三方会话（HTTP " + status.Code + "）。");
            }
            catch (Exception ex)
            {
                Log.Warn("注销第三方会话失败：" + ex.Message);
            }
        }

        /// <summary>
        /// JSON 字符串转义。除反斜杠与引号外，必须转义控制字符（U+0000–U+001F）：
        /// 密码里出现换行或制表符时，未转义会产生非法 JSON，不仅请求被拒，
        /// 还可能让服务端按不同结构解析，属于注入面。这里按 RFC 8259 完整处理。
        /// </summary>
        private static string JsonEscape(string value)
        {
            if (value == null)
            {
                return "";
            }
            System.Text.StringBuilder builder = new System.Text.StringBuilder(value.Length + 8);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                switch (c)
                {
                    case '"':
                        builder.Append("\\\"");
                        break;
                    case '\\':
                        builder.Append("\\\\");
                        break;
                    case '\b':
                        builder.Append("\\b");
                        break;
                    case '\f':
                        builder.Append("\\f");
                        break;
                    case '\n':
                        builder.Append("\\n");
                        break;
                    case '\r':
                        builder.Append("\\r");
                        break;
                    case '\t':
                        builder.Append("\\t");
                        break;
                    default:
                        if (c < ' ' || c == '\u007f')
                        {
                            builder.Append("\\u");
                            builder.Append(((int)c).ToString("x4",
                                System.Globalization.CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            builder.Append(c);
                        }
                        break;
                }
            }
            return builder.ToString();
        }
    }
}
