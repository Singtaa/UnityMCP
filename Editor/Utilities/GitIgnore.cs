using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace UnityMcp {
    /// <summary>
    /// Parses and evaluates .gitignore patterns.
    /// Used to prevent MCP from exposing sensitive files.
    /// </summary>
    public sealed class GitIgnore {
        // MARK: Pattern
        sealed class Pattern {
            public bool negated;
            public bool dirOnly;
            public bool anchored;
            public Regex regex;
            public string original; // For debugging
        }

        readonly List<Pattern> _patterns = new List<Pattern>();

        // MARK: Load
        public static GitIgnore LoadFromRootGitIgnore(string projectRoot) {
            var gi = new GitIgnore();

            var path = Path.Combine(projectRoot, ".gitignore");
            if (!File.Exists(path)) return gi;

            string[] lines;
            try {
                lines = File.ReadAllLines(path);
            } catch {
                return gi;
            }

            foreach (var raw in lines) {
                var line = raw.Trim();
                if (line.Length == 0) continue;
                if (line.StartsWith("#", StringComparison.Ordinal)) continue;

                var original = line;
                var negated = false;

                // Handle escaped hash
                if (line.StartsWith(@"\#", StringComparison.Ordinal)) {
                    line = line.Substring(1);
                }

                // Handle negation
                if (line.StartsWith("!", StringComparison.Ordinal)) {
                    negated = true;
                    line = line.Substring(1);
                } else if (line.StartsWith(@"\!", StringComparison.Ordinal)) {
                    line = line.Substring(1);
                }

                line = line.Trim();
                if (line.Length == 0) continue;

                // Directory-only patterns end with /
                var dirOnly = line.EndsWith("/", StringComparison.Ordinal);
                if (dirOnly) {
                    line = line.TrimEnd('/');
                }

                if (line.Length == 0) continue;

                // Anchored if starts with / or contains / (except trailing which we stripped)
                var anchored = line.StartsWith("/", StringComparison.Ordinal);
                if (anchored) {
                    line = line.Substring(1);
                }

                // Contains slash means it's a path pattern, not just basename
                var containsSlash = line.Contains("/");

                var regex = BuildRegex(line, anchored || containsSlash);

                gi._patterns.Add(new Pattern {
                    negated = negated,
                    dirOnly = dirOnly,
                    anchored = anchored || containsSlash,
                    regex = regex,
                    original = original
                });
            }

            return gi;
        }

        // MARK: Match
        public bool IsIgnored(string relUnix, bool isDirectory) {
            // Normalize path: forward slashes, no leading/trailing slashes
            var p = relUnix.Replace("\\", "/").Trim('/');
            if (string.IsNullOrEmpty(p)) return false;

            var ignored = false;

            for (var i = 0; i < _patterns.Count; i++) {
                var pat = _patterns[i];

                // Directory-only patterns skip files
                if (pat.dirOnly && !isDirectory) continue;

                bool matches;
                if (pat.anchored) {
                    // Anchored patterns match from root
                    matches = pat.regex.IsMatch(p);
                } else {
                    // Non-anchored patterns can match the full path OR just the basename
                    var basename = Path.GetFileName(p);
                    matches = pat.regex.IsMatch(p) || pat.regex.IsMatch(basename);
                }

                if (matches) {
                    ignored = !pat.negated;
                }
            }

            // Also check parent paths - if parent is ignored, children are too
            // (unless there's a negation pattern)
            if (!ignored) {
                var lastSlash = p.LastIndexOf('/');
                if (lastSlash > 0) {
                    var parent = p.Substring(0, lastSlash);
                    if (IsIgnored(parent, true)) {
                        // Check if there's a negation that un-ignores this specific path
                        // For simplicity, if parent is ignored, child is ignored
                        ignored = true;
                    }
                }
            }

            return ignored;
        }

        // MARK: Regex
        static Regex BuildRegex(string pattern, bool anchored) {
            // Convert gitignore glob to regex
            var regexPattern = GlobToRegex(pattern);

            string full;
            if (anchored) {
                // Must match from start
                full = "^" + regexPattern + "$";
            } else {
                // Can match anywhere (basename match) or full path
                full = "^" + regexPattern + "$";
            }

            return new Regex(full, RegexOptions.Compiled);
        }

        static string GlobToRegex(string glob) {
            var sb = new System.Text.StringBuilder();
            var i = 0;

            while (i < glob.Length) {
                var c = glob[i];

                if (c == '*') {
                    // Check for **
                    if (i + 1 < glob.Length && glob[i + 1] == '*') {
                        // ** matches everything including /
                        // Check if it's /**/
                        var beforeSlash = (i == 0) || (glob[i - 1] == '/');
                        var afterSlash = (i + 2 >= glob.Length) || (glob[i + 2] == '/');

                        if (beforeSlash && afterSlash) {
                            // Matches zero or more directories
                            sb.Append("(?:.*/)?");
                            i += 2;
                            if (i < glob.Length && glob[i] == '/') i++; // Skip trailing /
                            continue;
                        } else {
                            // Just ** without slashes - match everything
                            sb.Append(".*");
                            i += 2;
                            continue;
                        }
                    } else {
                        // Single * matches everything except /
                        sb.Append("[^/]*");
                        i++;
                    }
                } else if (c == '?') {
                    // ? matches any single char except /
                    sb.Append("[^/]");
                    i++;
                } else if (c == '[') {
                    // Character class - find closing ]
                    var end = glob.IndexOf(']', i + 1);
                    if (end > i) {
                        sb.Append(glob.Substring(i, end - i + 1));
                        i = end + 1;
                    } else {
                        sb.Append(Regex.Escape(c.ToString()));
                        i++;
                    }
                } else if (c == '/') {
                    sb.Append("/");
                    i++;
                } else {
                    // Escape regex special chars
                    sb.Append(Regex.Escape(c.ToString()));
                    i++;
                }
            }

            return sb.ToString();
        }

        // MARK: Debug
        public string DebugDump() {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Loaded {_patterns.Count} patterns:");
            foreach (var p in _patterns) {
                sb.AppendLine(
                    $"  {p.original} -> regex={p.regex}, dirOnly={p.dirOnly}, anchored={p.anchored}, negated={p.negated}");
            }
            return sb.ToString();
        }
    }

    /// <summary>
    /// Caches the GitIgnore instance and reloads when .gitignore changes.
    /// </summary>
    public static class GitIgnoreCache {
        static GitIgnore _cached;
        static DateTime _lastLoadUtc;

        public static GitIgnore Get() {
            var path = Path.Combine(ProjectPaths.ProjectRoot, ".gitignore");
            DateTime stamp = default;
            try {
                if (File.Exists(path)) stamp = File.GetLastWriteTimeUtc(path);
            } catch {
            }

            if (_cached == null || stamp > _lastLoadUtc) {
                _cached = GitIgnore.LoadFromRootGitIgnore(ProjectPaths.ProjectRoot);
                _lastLoadUtc = stamp;
            }
            return _cached;
        }
    }
}
