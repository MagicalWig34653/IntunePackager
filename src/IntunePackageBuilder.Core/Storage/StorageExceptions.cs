using System;

namespace IntunePackageBuilder.Core.Storage
{
    /// <summary>The file was written by a newer program version and must not be read or overwritten.</summary>
    public sealed class UnsupportedSchemaException : Exception
    {
        public UnsupportedSchemaException(string path, int foundVersion, int supportedVersion)
            : base("File '" + path + "' has schema version " + foundVersion + " but this program supports up to " + supportedVersion + ".")
        {
            Path = path;
            FoundVersion = foundVersion;
            SupportedVersion = supportedVersion;
        }

        public string Path { get; private set; }

        public int FoundVersion { get; private set; }

        public int SupportedVersion { get; private set; }
    }

    /// <summary>The file is not valid JSON, lacks a schema version, or has an unusable structure.</summary>
    public sealed class StorageFormatException : Exception
    {
        public StorageFormatException(string path, string reason, Exception inner = null)
            : base("File '" + path + "' is not readable: " + reason, inner)
        {
            Path = path;
        }

        public string Path { get; private set; }
    }

    /// <summary>No migration step exists for a version that is older than the supported one.</summary>
    public sealed class MigrationException : Exception
    {
        public MigrationException(int fromVersion)
            : base("No migration step from schema version " + fromVersion + ".")
        {
            FromVersion = fromVersion;
        }

        public int FromVersion { get; private set; }
    }
}
