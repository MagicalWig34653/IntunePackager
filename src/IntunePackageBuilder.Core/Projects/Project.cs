using System;

namespace IntunePackageBuilder.Core.Projects
{
    /// <summary>Content of <c>project.json</c>. Notes belong to the project, not to a software version.</summary>
    public sealed class Project
    {
        public const int CurrentSchemaVersion = 1;
        public const string FileName = "project.json";

        public int SchemaVersion { get; set; }

        public string ProjectId { get; set; }

        public string DisplayName { get; set; }

        public DateTime CreatedUtc { get; set; }

        public string Notes { get; set; }
    }
}
