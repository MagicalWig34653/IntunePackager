using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace IntunePackageBuilder.Core.Sources
{
    /// <summary>Builds and verifies source manifests.</summary>
    public static class SourceManifestBuilder
    {
        /// <summary>
        /// Hashes every file below <paramref name="sourceDirectory"/>. Junctions and symbolic links are
        /// never followed; finding one throws <see cref="UnsafeSourceException"/>.
        /// </summary>
        public static SourceManifest Build(string sourceDirectory)
        {
            return Build(sourceDirectory, DateTime.UtcNow);
        }

        internal static SourceManifest Build(string sourceDirectory, DateTime createdUtc)
        {
            var root = FullRoot(sourceDirectory);
            var manifest = new SourceManifest { CreatedUtc = createdUtc };
            foreach (var entry in Scan(root))
            {
                manifest.Files.Add(entry);
            }

            return manifest;
        }

        /// <summary>Compares the folder with a manifest and lists every difference (empty when unchanged).</summary>
        public static IReadOnlyList<SourceDifference> Compare(string sourceDirectory, SourceManifest manifest)
        {
            if (manifest == null)
            {
                throw new ArgumentNullException("manifest");
            }

            var root = FullRoot(sourceDirectory);
            var expected = new Dictionary<string, SourceFileEntry>(StringComparer.Ordinal);
            foreach (var file in manifest.Files)
            {
                expected[file.Path] = file;
            }

            var differences = new List<SourceDifference>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var actual in Scan(root))
            {
                seen.Add(actual.Path);
                SourceFileEntry stored;
                if (!expected.TryGetValue(actual.Path, out stored))
                {
                    differences.Add(new SourceDifference(actual.Path, SourceDifferenceKind.Added));
                }
                else if (stored.Size != actual.Size || !string.Equals(stored.Sha256, actual.Sha256, StringComparison.Ordinal))
                {
                    differences.Add(new SourceDifference(actual.Path, SourceDifferenceKind.Modified));
                }
            }

            foreach (var file in manifest.Files)
            {
                if (!seen.Contains(file.Path))
                {
                    differences.Add(new SourceDifference(file.Path, SourceDifferenceKind.Missing));
                }
            }

            differences.Sort((left, right) => string.CompareOrdinal(left.Path, right.Path));
            return differences;
        }

        /// <summary>Throws <see cref="SourceChangedException"/> unless the folder still matches the manifest.</summary>
        public static void RequireUnchanged(string sourceDirectory, SourceManifest manifest)
        {
            var differences = Compare(sourceDirectory, manifest);
            if (differences.Count > 0)
            {
                throw new SourceChangedException(differences);
            }
        }

        private static string FullRoot(string sourceDirectory)
        {
            if (string.IsNullOrWhiteSpace(sourceDirectory))
            {
                throw new ArgumentException("A source directory is required.", "sourceDirectory");
            }

            var root = Path.GetFullPath(sourceDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!Directory.Exists(root))
            {
                throw new DirectoryNotFoundException("The source directory does not exist: " + root);
            }

            return root;
        }

        private static List<SourceFileEntry> Scan(string root)
        {
            var entries = new List<SourceFileEntry>();
            ScanDirectory(root, root, entries);
            entries.Sort((left, right) => string.CompareOrdinal(left.Path, right.Path));
            return entries;
        }

        private static void ScanDirectory(string root, string directory, List<SourceFileEntry> entries)
        {
            foreach (var file in Directory.GetFiles(directory))
            {
                RejectReparsePoint(file);
                entries.Add(Describe(root, file));
            }

            foreach (var child in Directory.GetDirectories(directory))
            {
                RejectReparsePoint(child);
                ScanDirectory(root, child, entries);
            }
        }

        private static void RejectReparsePoint(string path)
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            {
                throw new UnsafeSourceException(path);
            }
        }

        private static SourceFileEntry Describe(string root, string file)
        {
            var relative = file.Substring(root.Length + 1).Replace('\\', '/');
            using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(stream);
                var builder = new StringBuilder(hash.Length * 2);
                foreach (var b in hash)
                {
                    builder.Append(b.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
                }

                return new SourceFileEntry { Path = relative, Size = stream.Length, Sha256 = builder.ToString() };
            }
        }
    }
}
