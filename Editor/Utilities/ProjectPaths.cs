using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Compilation;

namespace UnityMcp {
    /// <summary>
    /// Utilities for working with project paths safely.
    /// Prevents access to files outside the project root or files excluded by .gitignore.
    /// </summary>
    public static class ProjectPaths {
        // MARK: Root
        public static string ProjectRoot {
            get {
                var root = Directory.GetParent(UnityEngine.Application.dataPath).FullName;
                return NormalizeFull(root);
            }
        }

        // MARK: Public
        public static bool TryResolveAllowedPath(string relPath, bool isDirectory, GitIgnore ignore,
            out string fullPath, out string error) {
            fullPath = null;
            error = null;

            if (string.IsNullOrEmpty(relPath)) {
                error = "Empty path.";
                return false;
            }

            if (!IsSafeRelativePath(relPath, out var safeRel, out error)) return false;

            var root = ProjectRoot;
            var combined = Path.Combine(root, safeRel.Replace("/", Path.DirectorySeparatorChar.ToString()));
            var candidate = NormalizeFull(Path.GetFullPath(combined));

            if (!IsUnderRoot(root, candidate)) {
                error = "Path escapes project root.";
                return false;
            }

            var relUnix = safeRel.Replace("\\", "/");

            if (relUnix == ".git" || relUnix.StartsWith(".git/", StringComparison.Ordinal)) {
                error = "Path is under .git, always excluded.";
                return false;
            }

            if (ignore != null && ignore.IsIgnored(relUnix, isDirectory)) {
                error = "Path is excluded by root .gitignore.";
                return false;
            }

            fullPath = candidate;
            return true;
        }

        public static bool IsUnderAssets(string relUnix) {
            var p = relUnix.Replace("\\", "/");
            return p == "Assets" || p.StartsWith("Assets/", StringComparison.Ordinal);
        }

        public static string ToUnityAssetPath(string relUnix) {
            return relUnix.Replace("\\", "/");
        }

        public static void ScheduleRefreshAndCompilation(string relUnix) {
            var p = relUnix.Replace("\\", "/");

            if (IsUnderAssets(p)) {
                AssetDatabase.ImportAsset(p, ImportAssetOptions.ForceUpdate);
            } else {
                AssetDatabase.Refresh();
            }

            if (IsCodeRelated(p)) {
                CompilationPipeline.RequestScriptCompilation();
            }
        }

        public static bool IsCodeRelated(string relUnix) {
            var ext = Path.GetExtension(relUnix).ToLowerInvariant();
            return ext == ".cs" || ext == ".asmdef" || ext == ".asmref" || ext == ".rsp";
        }

        public static IEnumerable<(string path, string kind)> EnumerateProjectEntries(GitIgnore ignore) {
            var root = ProjectRoot;
            foreach (var e in EnumerateRecursive(root, root, ignore)) yield return e;
        }

        // MARK: Internal
        static IEnumerable<(string path, string kind)> EnumerateRecursive(string root, string dir,
            GitIgnore ignore) {
            IEnumerable<string> entries;
            try {
                entries = Directory.EnumerateFileSystemEntries(dir);
            } catch {
                yield break;
            }

            foreach (var full in entries) {
                var name = Path.GetFileName(full);
                if (name == ".git") continue;

                var isDir = Directory.Exists(full);
                var rel = GetRelativeUnix(root, full, isDir);

                if (ignore != null && ignore.IsIgnored(rel, isDir)) continue;

                yield return (rel, isDir ? "dir" : "file");

                if (isDir) {
                    foreach (var child in EnumerateRecursive(root, full, ignore)) yield return child;
                }
            }
        }

        static string GetRelativeUnix(string root, string full, bool isDir) {
            var rel = Path.GetRelativePath(root, full).Replace("\\", "/");
            rel = rel.TrimEnd('/');
            if (isDir) rel += "/";
            return rel;
        }

        static bool IsSafeRelativePath(string input, out string safeRel, out string error) {
            safeRel = null;
            error = null;

            var p = input.Replace("\\", "/").Trim();

            if (p.StartsWith("/", StringComparison.Ordinal) || p.StartsWith("~", StringComparison.Ordinal)) {
                error = "Absolute paths are not allowed.";
                return false;
            }

            if (p.Contains("\0")) {
                error = "Invalid path.";
                return false;
            }

            if (p.Length >= 2 && char.IsLetter(p[0]) && p[1] == ':') {
                error = "Drive paths are not allowed.";
                return false;
            }

            var parts = p.Split('/');
            foreach (var part in parts) {
                if (part == "..") {
                    error = "Path traversal ('..') is not allowed.";
                    return false;
                }
            }

            safeRel = p.TrimStart('.');
            safeRel = safeRel.TrimStart('/');

            if (string.IsNullOrEmpty(safeRel)) {
                error = "Invalid relative path.";
                return false;
            }

            return true;
        }

        static string NormalizeFull(string full) {
            return full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        static bool IsUnderRoot(string root, string candidate) {
            var rootWithSep = root + Path.DirectorySeparatorChar;

            var comparison = UnityEngine.Application.platform == UnityEngine.RuntimePlatform.WindowsEditor
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

            if (string.Equals(candidate, root, comparison)) return true;
            return candidate.StartsWith(rootWithSep, comparison);
        }
    }
}
