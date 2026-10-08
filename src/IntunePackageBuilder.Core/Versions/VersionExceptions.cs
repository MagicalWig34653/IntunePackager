using System;

namespace IntunePackageBuilder.Core.Versions
{
    public sealed class VersionExistsException : Exception
    {
        public VersionExistsException(string projectId, string version)
            : base("Version '" + version + "' of project '" + projectId + "' already exists.")
        {
            ProjectId = projectId;
            Version = version;
        }

        public string ProjectId { get; private set; }

        public string Version { get; private set; }
    }

    public sealed class VersionNotFoundException : Exception
    {
        public VersionNotFoundException(string projectId, string version)
            : base("Version '" + version + "' of project '" + projectId + "' was not found.")
        {
            ProjectId = projectId;
            Version = version;
        }

        public string ProjectId { get; private set; }

        public string Version { get; private set; }
    }
}
