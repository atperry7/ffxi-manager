using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FFXIManager.Utilities
{
    public static class ProcessFilters
    {
        private static readonly string[] DefaultIgnoredWindowTitles = new[]
        {
            "Default IME", "MSCTFIME UI", "Program Manager"
        };

        public static bool WildcardMatch(string input, string pattern)
        {
            if (pattern == null) return false;
            if (string.IsNullOrEmpty(pattern) || pattern == "*") return true;
            // Escape regex and replace wildcard
            var regex = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
                .Replace("\\*", ".*") + "$";
            return System.Text.RegularExpressions.Regex.IsMatch(input ?? string.Empty, regex, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }

        public static bool MatchesNamePatterns(string? candidate, IEnumerable<string> includePatterns, IEnumerable<string> excludePatterns)
        {
            var name = ExtractProcessName(candidate);
            if (string.IsNullOrWhiteSpace(name)) return false;

            var includes = (includePatterns ?? Array.Empty<string>()).ToList();
            var excludes = (excludePatterns ?? Array.Empty<string>()).ToList();

            bool included = includes.Count == 0 || includes.Any(p => WildcardMatch(name, ExtractProcessName(p)));
            if (!included) return false;
            bool excluded = excludes.Any(p => WildcardMatch(name, ExtractProcessName(p)));
            return !excluded;
        }

        public static string ExtractProcessName(string? executablePathOrName)
        {
            if (string.IsNullOrWhiteSpace(executablePathOrName)) return string.Empty;
            try
            {
                // If a path, reduce to filename without extension
                var fileName = executablePathOrName!
                    .Replace("\"", string.Empty)
                    .Trim();
                if (fileName.Contains(Path.DirectorySeparatorChar) || fileName.Contains(Path.AltDirectorySeparatorChar))
                {
                    fileName = Path.GetFileNameWithoutExtension(fileName);
                }
                else
                {
                    // It may already be a process name; strip extension if present
                    fileName = Path.GetFileNameWithoutExtension(fileName);
                }
                return fileName ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        // Loose normalization: lowercase and remove non-alphanumeric characters
        public static string NormalizeComparable(string? nameOrPath)
        {
            var core = ExtractProcessName(nameOrPath);
            if (string.IsNullOrWhiteSpace(core)) return string.Empty;
            var sb = new System.Text.StringBuilder(core.Length);
            foreach (var ch in core)
            {
                if (char.IsLetterOrDigit(ch)) sb.Append(char.ToLowerInvariant(ch));
            }
            return sb.ToString();
        }

        public static HashSet<string> ToNameSet(IEnumerable<string> names)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var n in names ?? Array.Empty<string>())
            {
                var normalized = ExtractProcessName(n);
                if (!string.IsNullOrWhiteSpace(normalized)) set.Add(normalized);
            }
            return set;
        }

        public static HashSet<string> ToLooseNameSet(IEnumerable<string> names)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            foreach (var n in names ?? Array.Empty<string>())
            {
                var normalized = NormalizeComparable(n);
                if (!string.IsNullOrWhiteSpace(normalized)) set.Add(normalized);
            }
            return set;
        }

        public static bool MatchesProcessName(string? candidate, IEnumerable<string> names)
        {
            if (string.IsNullOrWhiteSpace(candidate)) return false;
            var normalized = ExtractProcessName(candidate);
            if (string.IsNullOrWhiteSpace(normalized)) return false;
            var set = ToNameSet(names);
            return set.Contains(normalized);
        }

        public static bool MatchesProcessNameLoose(string? candidate, IEnumerable<string> names)
        {
            if (string.IsNullOrWhiteSpace(candidate)) return false;
            var normalized = NormalizeComparable(candidate);
            if (string.IsNullOrWhiteSpace(normalized)) return false;
            var set = ToLooseNameSet(names);
            return set.Contains(normalized);
        }

        public static bool IsAcceptableWindowTitle(string? title)
        {
            if (string.IsNullOrWhiteSpace(title)) return false;
            // Use default ignore list only
            foreach (var ignored in DefaultIgnoredWindowTitles)
            {
                if (title!.StartsWith(ignored, StringComparison.Ordinal)) return false;
            }
            return true;
        }
    }
}

