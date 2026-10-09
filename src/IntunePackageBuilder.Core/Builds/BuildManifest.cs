using System;
using System.Collections.Generic;
using IntunePackageBuilder.Core.Storage;

namespace IntunePackageBuilder.Core.Builds
{
    public sealed class BuildFileEntry
    {
        /// <summary>Path relative to the build folder, with <c>/</c> as separator.</summary>
        public string Path { get; set; }

        public long Size { get; set; }

        /// <summary>SHA-256 as 64 lower-case hexadecimal characters.</summary>
        public string Sha256 { get; set; }
    }

    /// <summary>
    /// Content of <c>build-manifest.json</c>: what one build produced and with which inputs and tools, so a result can
    /// be verified later (SPEC sections 6.1, 7.2, 12.1 A18). Written once after the files it lists; never replaced.
    /// </summary>
    public sealed class BuildManifest
    {
        public const int CurrentSchemaVersion = 1;
        public const string FileName = "build-manifest.json";

        public BuildManifest()
        {
            SchemaVersion = CurrentSchemaVersion;
            Files = new List<BuildFileEntry>();
        }

        public int SchemaVersion { get; set; }

        public string BuildId { get; set; }

        public DateTime CreatedUtc { get; set; }

        public string ProjectId { get; set; }

        /// <summary>Target version of the software (not the build ID).</summary>
        public string SoftwareVersion { get; set; }

        public string Language { get; set; }

        /// <summary>Version of the authoring tool.</summary>
        public string ToolVersion { get; set; }

        /// <summary>Fingerprint of the stored source this build used (see <c>source-manifest.json</c>).</summary>
        public string SourceFingerprint { get; set; }

        /// <summary>Known version of the Content Prep Tool, or missing when its SHA-256 was not a known version.</summary>
        public string ContentPrepToolVersion { get; set; }

        public string ContentPrepToolSha256 { get; set; }

        /// <summary>The <c>.intunewin</c> file.</summary>
        public BuildFileEntry Package { get; set; }

        /// <summary>All other files of the build folder except this manifest and <c>build.log</c>.</summary>
        public List<BuildFileEntry> Files { get; set; }
    }

    /// <summary>Reads and writes <c>build-manifest.json</c>.</summary>
    public static class BuildManifestStore
    {
        public static void WriteNew(string path, BuildManifest manifest)
        {
            if (manifest == null)
            {
                throw new ArgumentNullException("manifest");
            }

            if (System.IO.File.Exists(path))
            {
                throw new System.IO.IOException("A build manifest already exists: '" + path + "'. A manifest is never replaced.");
            }

            manifest.SchemaVersion = BuildManifest.CurrentSchemaVersion;
            VersionedJsonFile.Write(path, manifest);
        }

        public static BuildManifest Read(string path)
        {
            return VersionedJsonFile.Read<BuildManifest>(
                path,
                BuildManifest.CurrentSchemaVersion,
                new IMigrationStep[0],
                loaded =>
                {
                    if (!BuildId.IsValid(loaded.BuildId) || loaded.Package == null || string.IsNullOrEmpty(loaded.Package.Sha256))
                    {
                        throw new StorageFormatException(path, "the manifest lacks a valid build ID or its package entry");
                    }
                });
        }
    }
}
