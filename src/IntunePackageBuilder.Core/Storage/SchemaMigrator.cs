using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace IntunePackageBuilder.Core.Storage
{
    /// <summary>One step upgrades a document from <see cref="FromVersion"/> to <c>FromVersion + 1</c>.</summary>
    public interface IMigrationStep
    {
        int FromVersion { get; }

        void Apply(JObject document);
    }

    public sealed class MigrationResult
    {
        public MigrationResult(int fromVersion, int toVersion)
        {
            FromVersion = fromVersion;
            ToVersion = toVersion;
        }

        public int FromVersion { get; private set; }

        public int ToVersion { get; private set; }

        public bool Migrated
        {
            get { return ToVersion != FromVersion; }
        }
    }

    public static class SchemaMigrator
    {
        public const string VersionProperty = "schemaVersion";

        /// <summary>Reads the schema version, or returns false when it is missing or not a positive integer.</summary>
        public static bool TryReadVersion(JObject document, out int version)
        {
            version = 0;
            JToken token;
            if (!document.TryGetValue(VersionProperty, out token) || token.Type != JTokenType.Integer)
            {
                return false;
            }

            version = token.Value<int>();
            return version > 0;
        }

        /// <summary>
        /// Upgrades the document in memory to <paramref name="currentVersion"/>.
        /// A newer document is rejected so that it is never rewritten by an older program.
        /// </summary>
        public static MigrationResult Migrate(string path, JObject document, int currentVersion, IReadOnlyList<IMigrationStep> steps)
        {
            int found;
            if (!TryReadVersion(document, out found))
            {
                throw new StorageFormatException(path, "missing or invalid '" + VersionProperty + "'");
            }

            if (found > currentVersion)
            {
                throw new UnsupportedSchemaException(path, found, currentVersion);
            }

            var version = found;
            while (version < currentVersion)
            {
                IMigrationStep step = null;
                foreach (var candidate in steps)
                {
                    if (candidate.FromVersion == version)
                    {
                        step = candidate;
                        break;
                    }
                }

                if (step == null)
                {
                    throw new MigrationException(version);
                }

                step.Apply(document);
                version++;
                document[VersionProperty] = version;
            }

            return new MigrationResult(found, version);
        }
    }
}
