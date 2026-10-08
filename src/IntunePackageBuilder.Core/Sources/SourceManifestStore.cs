using System;
using System.IO;
using IntunePackageBuilder.Core.Storage;

namespace IntunePackageBuilder.Core.Sources
{
    /// <summary>Reads and writes <c>source-manifest.json</c>. A written manifest is never replaced silently.</summary>
    public static class SourceManifestStore
    {
        /// <summary>
        /// Writes a new manifest. An existing manifest is an immutable record of the stored source, so
        /// writing over it is refused (a changed source needs a new version).
        /// </summary>
        public static void WriteNew(string path, SourceManifest manifest)
        {
            if (manifest == null)
            {
                throw new ArgumentNullException("manifest");
            }

            if (File.Exists(path))
            {
                throw new IOException("A source manifest already exists: '" + path + "'. A changed source needs a new version.");
            }

            manifest.SchemaVersion = SourceManifest.CurrentSchemaVersion;
            VersionedJsonFile.Write(path, manifest);
        }

        public static SourceManifest Read(string path)
        {
            return VersionedJsonFile.Read<SourceManifest>(
                path,
                SourceManifest.CurrentSchemaVersion,
                new IMigrationStep[0],
                loaded =>
                {
                    foreach (var file in loaded.Files)
                    {
                        if (file == null || string.IsNullOrEmpty(file.Path) || string.IsNullOrEmpty(file.Sha256))
                        {
                            throw new StorageFormatException(path, "a file entry lacks its path or checksum");
                        }
                    }
                });
        }
    }
}
