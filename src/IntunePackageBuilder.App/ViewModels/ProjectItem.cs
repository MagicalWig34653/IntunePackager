using System;

namespace IntunePackageBuilder.App.ViewModels
{
    /// <summary>A project in the lists of the start page.</summary>
    public sealed class ProjectItem
    {
        public string ProjectId { get; set; }

        public string DisplayName { get; set; }

        /// <summary>Last opened (recent list) or created (all projects); null when unknown.</summary>
        public DateTime? Date { get; set; }

        /// <summary>The project could not be read; it is listed but marked.</summary>
        public bool IsBroken { get; set; }
    }
}
