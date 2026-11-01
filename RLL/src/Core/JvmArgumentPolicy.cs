using System;
using System.Collections.Generic;

namespace LiteLauncher.Core
{
    /// <summary>
    /// JVM 参数安全策略：用户自定义参数、Fabric profile、版本 JSON 三类来源
    /// 在启动前都经过这里校验。
    ///
    /// 匹配方式为"参数名精确匹配"而非子串包含。旧实现用 IndexOf 子串匹配，
    /// 会把 -Dmaven.repo.local=C:\my-cp-repo 这类合法属性值误判为 -cp，
    /// 同时又漏掉 --add-opens、-Djava.system.class.loader 等能实现类加载劫持的等价写法。
    ///
    /// 两级强度：
    /// - IsCritical：会注入/替换可执行代码或在崩溃时执行命令的参数。对全部来源生效。
    /// - IsBanned：在此之上再禁止模块系统与类路径改写。对用户输入与 Fabric profile 生效，
    ///   避开版本 JSON —— Mojang 清单经 SHA1 校验且未来版本可能合法使用 --add-opens。
    /// </summary>
    public static class JvmArgumentPolicy
    {
        /// <summary>
        /// 关键参数名（小写）：可直接加载任意本地代码、替换引导类路径、指定主类，
        /// 或在崩溃 / OOM 时执行任意命令。对全部来源生效。
        /// </summary>
        private static readonly string[] CriticalNames = new string[]
        {
            "-javaagent",
            "-agentlib",
            "-agentpath",
            "-xbootclasspath",
            "-xbootclasspath/a",
            "-xbootclasspath/p",
            "-djava.system.class.loader",
            "-dsun.boot.class.path",
            "-cp",
            "-classpath",
            "-jar",
            "-xx:onerror",
            "-xx:onoutofmemoryerror",
            // 让 JVM 读取额外的参数文件，绕过本启动器的参数拼装
            "-djdk.java.options",
            "-djava.tool.options"
        };

        /// <summary>
        /// 关键参数的前缀形式（含冒号或等号后接值），如 -javaagent:C:\x.jar。
        /// </summary>
        private static readonly string[] CriticalPrefixes = new string[]
        {
            "-javaagent:",
            "-agentlib:",
            "-agentpath:",
            "-xbootclasspath:",
            "-xbootclasspath/a:",
            "-xbootclasspath/p:",
            "-djava.system.class.loader=",
            "-dsun.boot.class.path=",
            "-xx:onerror=",
            "-xx:onoutofmemoryerror=",
            "-djdk.java.options=",
            "-djava.tool.options="
        };

        /// <summary>
        /// 扩展禁止项：模块系统与类路径改写。仅用于用户输入与 Fabric profile，
        /// 避免误拦 Mojang 版本 JSON 中合法的模块参数。
        /// </summary>
        private static readonly string[] ExtendedNames = new string[]
        {
            "--add-opens",
            "--add-exports",
            "--add-reads",
            "--patch-module",
            "--module-path",
            "--upgrade-module-path",
            "-p",
            "-dbootclasspath"
        };

        private static readonly string[] ExtendedPrefixes = new string[]
        {
            "--add-opens=",
            "--add-exports=",
            "--add-reads=",
            "--patch-module=",
            "--module-path=",
            "--upgrade-module-path=",
            "-dbootclasspath="
        };

        /// <summary>
        /// 取参数名：去掉首尾空白，截断到第一个空格、'=' 或 ':' 之前，转小写。
        /// 例：-XX:OnError=calc.exe → -xx；-Dfoo=-cp → -dfoo；--add-opens=x → --add-opens。
        /// </summary>
        public static string GetArgumentName(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "";
            }
            string name = value.Trim();
            if (name.Length == 0)
            {
                return "";
            }
            // 只取第一个空白前的片段，避免 "-Xmx4G -javaagent:evil.jar" 这种拼接绕过。
            int space = name.IndexOf(' ');
            if (space > 0)
            {
                name = name.Substring(0, space);
            }
            int eq = name.IndexOf('=');
            if (eq > 0)
            {
                name = name.Substring(0, eq);
            }
            return name.ToLowerInvariant();
        }

        /// <summary>
        /// 关键级判断：任一步骤发现危险参数即返回 true。对全部来源生效。
        /// </summary>
        public static bool IsCritical(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }
            string trimmed = value.Trim();
            if (trimmed.Length == 0)
            {
                return false;
            }
            string lower = trimmed.ToLowerInvariant();
            string name = GetArgumentName(trimmed);

            for (int i = 0; i < CriticalNames.Length; i++)
            {
                if (string.Equals(name, CriticalNames[i], StringComparison.Ordinal))
                {
                    return true;
                }
            }
            for (int i = 0; i < CriticalPrefixes.Length; i++)
            {
                if (lower.StartsWith(CriticalPrefixes[i], StringComparison.Ordinal))
                {
                    return true;
                }
            }

            // 形如 -Xbootclasspath/a:evil.jar：参数名是 -xbootclasspath/a:evil.jar，
            // 按冒号再截一次后与黑名单比对。
            int colon = name.IndexOf(':');
            if (colon > 0)
            {
                string head = name.Substring(0, colon);
                for (int i = 0; i < CriticalNames.Length; i++)
                {
                    if (string.Equals(head, CriticalNames[i], StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// 完整判断：关键级 + 扩展级。用于用户输入与 Fabric profile。
        /// </summary>
        public static bool IsBanned(string value)
        {
            if (IsCritical(value))
            {
                return true;
            }
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }
            string trimmed = value.Trim();
            if (trimmed.Length == 0)
            {
                return false;
            }
            string lower = trimmed.ToLowerInvariant();
            string name = GetArgumentName(trimmed);

            for (int i = 0; i < ExtendedNames.Length; i++)
            {
                if (string.Equals(name, ExtendedNames[i], StringComparison.Ordinal))
                {
                    return true;
                }
            }
            for (int i = 0; i < ExtendedPrefixes.Length; i++)
            {
                if (lower.StartsWith(ExtendedPrefixes[i], StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 在一组参数中查找第一个危险项，返回它；全部安全则返回 null。
        /// </summary>
        public static string FindBanned(IEnumerable<string> values, bool includeExtended)
        {
            if (values == null)
            {
                return null;
            }
            foreach (string value in values)
            {
                bool hit = includeExtended ? IsBanned(value) : IsCritical(value);
                if (hit)
                {
                    return value;
                }
            }
            return null;
        }
    }
}
