using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace IntunePackageBuilder.Core.Sources
{
    public sealed class SourceFileEntry
    {
        /// <summary>Path relative to the source folder, with <c>/</c> as separator.</summary>
        public string Path { get; set; }

        public long Size { get; set; }

        /// <summary>SHA-256 as 64 lower-case hexadecimal characters.</summary>
        public string Sha256 { get; set; }
    }

    /// <summary>
    /// Content of <c>source-manifest.json</c>: every file of a stored installation source with size and
    /// checksum, so later builds can prove the stored source has not changed (SPEC sections 6.4, 7.2, 7.3).
    /// </summary>
    public sealed class SourceManifest
    {
        public const int CurrentSchemaVersion = 1;
        public const string FileName = "source-manifest.json";

        public SourceManifest()
        {
            SchemaVersion = CurrentSchemaVersion;
            Files = new List<SourceFileEntry>();
        }

        public int SchemaVersion { get; set; }

        public DateTime CreatedUtc { get; set; }

        /// <summary>All files, sorted by path (ordinal).</summary>
        public List<SourceFileEntry> Files { get; set; }

        /// <summary>
        /// SHA-256 over every file's path, size and checksum (sorted by path). The timestamp is not part of it,
        /// so the same source always gives the same fingerprint. A build records it to prove which source it used.
        /// </summary>
        public string ComputeFingerprint()
        {
            var ordered = new List<SourceFileEntry>(Files);
            ordered.Sort((left, right) => string.CompareOrdinal(left.Path, right.Path));

            var text = new StringBuilder();
            foreach (var file in ordered)
            {
                text.Append(file.Path).Append('\n')
                    .Append(file.Size.ToString(CultureInfo.InvariantCulture)).Append('\n')
                    .Append(file.Sha256).Append('\n');
            }

            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(new UTF8Encoding(false).GetBytes(text.ToString()));
                var hex = new StringBuilder(hash.Length * 2);
                foreach (var b in hash)
                {
                    hex.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                }

                return hex.ToString();
            }
        }
    }

    public enum SourceDifferenceKind
    {
        /// <summary>The file is in the manifest but no longer in the folder.</summary>
        Missing,

        /// <summary>The file is in the folder but not in the manifest.</summary>
        Added,

        /// <summary>The file exists but its size or checksum differs.</summary>
        Modified
    }

    public sealed class SourceDifference
    {
        public SourceDifference(string path, SourceDifferenceKind kind)
        {
            Path = path;
            Kind = kind;
        }

        public string Path { get; private set; }

        public SourceDifferenceKind Kind { get; private set; }

        public override string ToString()
        {
            return Kind + ": " + Path;
        }
    }

    /// <summary>The stored source differs from its manifest; building from it would use unverified files.</summary>
    public sealed class SourceChangedException : Exception
    {
        public SourceChangedException(IReadOnlyList<SourceDifference> differences)
            : base("The stored source changed since its manifest was written (" + differences.Count + " difference(s)).")
        {
            Differences = differences;
        }

        public IReadOnlyList<SourceDifference> Differences { get; private set; }
    }

    /// <summary>The source folder contains something the manifest must not follow (junction or symbolic link).</summary>
    public sealed class UnsafeSourceException : Exception
    {
        public UnsafeSourceException(string path)
            : base("The source contains a junction or symbolic link: '" + path + "'.")
        {
            Path = path;
        }

        public string Path { get; private set; }
    }
}
