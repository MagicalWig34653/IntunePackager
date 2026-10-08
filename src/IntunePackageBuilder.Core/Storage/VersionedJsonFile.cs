using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace IntunePackageBuilder.Core.Storage
{
    /// <summary>
    /// Reads and writes JSON documents that carry a <c>schemaVersion</c>: migration with backup on read,
    /// atomic write, and protection against overwriting files written by a newer program version.
    /// </summary>
    internal static class VersionedJsonFile
    {
        /// <summary>
        /// Reads a document and upgrades it to <paramref name="currentVersion"/>. When a migration ran,
        /// the original is first copied to <c>&lt;file&gt;.v&lt;old&gt;.bak</c> and the migrated file is written
        /// afterwards. <paramref name="validate"/> runs before anything is persisted.
        /// </summary>
        public static T Read<T>(string path, int currentVersion, IReadOnlyList<IMigrationStep> steps, Action<T> validate = null)
            where T : class
        {
            var document = ParseFile(path);
            var result = SchemaMigrator.Migrate(path, document, currentVersion, steps);

            T value;
            try
            {
                value = document.ToObject<T>(JsonFormat.CreateSerializer());
            }
            catch (JsonException ex)
            {
                throw new StorageFormatException(path, "unexpected structure", ex);
            }

            if (value == null)
            {
                throw new StorageFormatException(path, "empty document");
            }

            if (validate != null)
            {
                validate(value);
            }

            if (result.Migrated)
            {
                BackUp(path, result.FromVersion);
                Write(path, value);
            }

            return value;
        }

        public static void Write(string path, object value)
        {
            var json = JsonConvert.SerializeObject(value, JsonFormat.CreateSettings());
            AtomicFile.WriteAllText(path, json + Environment.NewLine);
        }

        /// <summary>Throws <see cref="UnsupportedSchemaException"/> when the existing file is newer than supported.</summary>
        public static void GuardAgainstNewerSchema(string path, int currentVersion)
        {
            var document = ParseFile(path);
            int found;
            if (SchemaMigrator.TryReadVersion(document, out found) && found > currentVersion)
            {
                throw new UnsupportedSchemaException(path, found, currentVersion);
            }
        }

        private static JObject ParseFile(string path)
        {
            try
            {
                return JsonFormat.ParseObject(File.ReadAllText(path));
            }
            catch (JsonReaderException ex)
            {
                throw new StorageFormatException(path, "invalid JSON", ex);
            }
        }

        private static void BackUp(string path, int fromVersion)
        {
            var backup = path + ".v" + fromVersion + ".bak";
            if (!File.Exists(backup))
            {
                File.Copy(path, backup);
            }
        }
    }
}
