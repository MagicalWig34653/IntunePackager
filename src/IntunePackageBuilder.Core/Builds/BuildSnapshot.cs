using System;
using IntunePackageBuilder.Core.Sources;
using IntunePackageBuilder.Core.Storage;
using IntunePackageBuilder.Core.Versions;
using Newtonsoft.Json;

namespace IntunePackageBuilder.Core.Builds
{
    /// <summary>
    /// Everything that determines the output of one build: a private copy of the configuration, the build ID,
    /// the language, the tool version and the fingerprint of the source. It is written once as
    /// <c>configuration.snapshot.json</c> next to the build result and never changed, so earlier results stay
    /// explainable even when the configuration of the version is edited later (SPEC section 6.4).
    /// All generated files (detection script, guide, settings) are produced from this snapshot, never from UI state.
    /// </summary>
    public sealed class BuildSnapshot
    {
        public const int CurrentSchemaVersion = 1;
        public const string FileName = "configuration.snapshot.json";

        public static readonly string[] SupportedLanguages = { "de", "en" };

        public int SchemaVersion { get; set; }

        public string BuildId { get; set; }

        public DateTime CreatedUtc { get; set; }

        /// <summary>Language of the generated texts (<c>de</c> or <c>en</c>), fixed at build time.</summary>
        public string Language { get; set; }

        public string ToolVersion { get; set; }

        /// <summary>See <see cref="SourceManifest.ComputeFingerprint"/>.</summary>
        public string SourceFingerprint { get; set; }

        public PackageVersionConfig Configuration { get; set; }

        /// <summary>Creates a snapshot with its own deep copy of the configuration.</summary>
        public static BuildSnapshot Create(
            PackageVersionConfig configuration,
            SourceManifest sourceManifest,
            string buildId,
            string language,
            DateTime createdUtc)
        {
            if (configuration == null)
            {
                throw new ArgumentNullException("configuration");
            }

            if (sourceManifest == null)
            {
                throw new ArgumentNullException("sourceManifest");
            }

            if (!Builds.BuildId.IsValid(buildId))
            {
                throw new ArgumentException("'" + buildId + "' is not a valid build ID.", "buildId");
            }

            if (Array.IndexOf(SupportedLanguages, language) < 0)
            {
                throw new ArgumentException("'" + language + "' is not a supported language.", "language");
            }

            var settings = JsonFormat.CreateSettings();
            var copy = JsonConvert.DeserializeObject<PackageVersionConfig>(
                JsonConvert.SerializeObject(configuration, settings), settings);

            return new BuildSnapshot
            {
                SchemaVersion = CurrentSchemaVersion,
                BuildId = buildId,
                CreatedUtc = createdUtc,
                Language = language,
                ToolVersion = AppInfo.Version,
                SourceFingerprint = sourceManifest.ComputeFingerprint(),
                Configuration = copy
            };
        }
    }

    /// <summary>Reads and writes <c>configuration.snapshot.json</c>. A written snapshot is never replaced.</summary>
    public static class BuildSnapshotStore
    {
        public static void WriteNew(string path, BuildSnapshot snapshot)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException("snapshot");
            }

            if (System.IO.File.Exists(path))
            {
                throw new System.IO.IOException("A build snapshot already exists: '" + path + "'. A snapshot is never replaced.");
            }

            snapshot.SchemaVersion = BuildSnapshot.CurrentSchemaVersion;
            VersionedJsonFile.Write(path, snapshot);
        }

        public static BuildSnapshot Read(string path)
        {
            return VersionedJsonFile.Read<BuildSnapshot>(
                path,
                BuildSnapshot.CurrentSchemaVersion,
                new IMigrationStep[0],
                loaded =>
                {
                    if (!BuildId.IsValid(loaded.BuildId) || loaded.Configuration == null)
                    {
                        throw new StorageFormatException(path, "the snapshot lacks a valid build ID or its configuration");
                    }
                });
        }
    }
}
