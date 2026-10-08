using System;
using System.Collections.Generic;
using System.IO;

namespace IntunePackageBuilder.Core.Sources
{
    /// <summary>Path checks shared by the source manifest and the source import.</summary>
    internal static class PathSafety
    {
        public static string Full(string path)
        {
            var full = Path.GetFullPath(path);
            var root = Path.GetPathRoot(full);
            return full.Length > root.Length
                ? full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                : full;
        }

        /// <summary>True when <paramref name="child"/> is the same folder as <paramref name="parent"/> or lies below it.</summary>
        public static bool IsSameOrInside(string parent, string child)
        {
            var p = WithSeparator(Full(parent));
            var c = WithSeparator(Full(child));
            return c.StartsWith(p, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Path of <paramref name="file"/> relative to <paramref name="root"/>, with <c>/</c> as separator. Works for drive roots too.</summary>
        public static string Relative(string root, string file)
        {
            var prefix = WithSeparator(Full(root));
            return Full(file).Substring(prefix.Length).Replace('\\', '/');
        }

        public static bool IsReparsePoint(string path)
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }

        /// <summary>
        /// Lists every file below <paramref name="root"/>. The root and everything below it must be free of
        /// junctions and symbolic links; finding one throws <see cref="UnsafeSourceException"/> and nothing is followed.
        /// </summary>
        public static List<string> ListFiles(string root)
        {
            if (IsReparsePoint(root))
            {
                throw new UnsafeSourceException(root);
            }

            var files = new List<string>();
            Walk(root, files);
            return files;
        }

        private static void Walk(string directory, List<string> files)
        {
            foreach (var file in Directory.GetFiles(directory))
            {
                if (IsReparsePoint(file))
                {
                    throw new UnsafeSourceException(file);
                }

                files.Add(file);
            }

            foreach (var child in Directory.GetDirectories(directory))
            {
                if (IsReparsePoint(child))
                {
                    throw new UnsafeSourceException(child);
                }

                Walk(child, files);
            }
        }

        private static string WithSeparator(string path)
        {
            return path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? path
                : path + Path.DirectorySeparatorChar;
        }
    }
}
