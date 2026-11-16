using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace LiteLauncher.Core
{
    public static class RuleEvaluator
    {
        public static string CurrentOsName
        {
            get { return "windows"; }
        }

        public static string CurrentOsArch
        {
            get
            {
                string architecture = Environment.GetEnvironmentVariable("PROCESSOR_ARCHITECTURE") ?? "";
                if (architecture.IndexOf("ARM64", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return "arm64";
                }
                if (architecture.IndexOf("x86", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    architecture.IndexOf("64", StringComparison.Ordinal) < 0)
                {
                    return "x86";
                }
                return "x86_64";
            }
        }

        public static bool IsAllowed(List<Rule> rules, IDictionary<string, bool> features)
        {
            if (rules == null || rules.Count == 0)
            {
                return true;
            }
            bool allowed = false;
            for (int i = 0; i < rules.Count; i++)
            {
                Rule rule = rules[i];
                if (rule == null || !Matches(rule, features))
                {
                    continue;
                }
                allowed = string.Equals(rule.Action, "allow", StringComparison.OrdinalIgnoreCase);
            }
            return allowed;
        }

        private static bool Matches(Rule rule, IDictionary<string, bool> features)
        {
            if (!string.IsNullOrEmpty(rule.OsName) &&
                !string.Equals(rule.OsName, CurrentOsName, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            if (!string.IsNullOrEmpty(rule.OsArch) &&
                !string.Equals(rule.OsArch, CurrentOsArch, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            if (!string.IsNullOrEmpty(rule.OsVersion))
            {
                try
                {
                    if (!Regex.IsMatch(
                        Environment.OSVersion.Version.ToString(),
                        rule.OsVersion,
                        RegexOptions.None,
                        TimeSpan.FromSeconds(1)))
                    {
                        return false;
                    }
                }
                catch (ArgumentException)
                {
                    return false;
                }
                catch (RegexMatchTimeoutException)
                {
                    return false;
                }
            }

            if (rule.Features != null)
            {
                foreach (KeyValuePair<string, bool> pair in rule.Features)
                {
                    bool actual = false;
                    if (features != null)
                    {
                        features.TryGetValue(pair.Key, out actual);
                    }
                    if (actual != pair.Value)
                    {
                        return false;
                    }
                }
            }
            return true;
        }
    }
}
