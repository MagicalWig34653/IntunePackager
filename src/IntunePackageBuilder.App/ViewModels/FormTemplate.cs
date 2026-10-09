using IntunePackageBuilder.Core.Versions;

namespace IntunePackageBuilder.App.ViewModels
{
    public enum FormMode
    {
        /// <summary>A new package from a dropped installer; the project is created with the build.</summary>
        NewPackage,

        /// <summary>A new version of an existing project that adopts the settings of a base version (SPEC 6.5).</summary>
        Update,

        /// <summary>A new version of an existing project without a template.</summary>
        NewVersion,

        /// <summary>The same version built again from its stored source; the version number is fixed.</summary>
        Rebuild
    }

    /// <summary>What the form needs besides the analysis of the installer when it belongs to an existing project.</summary>
    public sealed class FormTemplate
    {
        public FormMode Mode { get; set; }

        /// <summary>The project the new package belongs to; null for a new project.</summary>
        public string ExistingProjectId { get; set; }

        /// <summary>The draft to start from instead of the plain analysis (update and rebuild); a private copy is used.</summary>
        public PackageVersionConfig Draft { get; set; }

        /// <summary>The version the draft comes from; shown in the heading and the hint.</summary>
        public string BasisVersion { get; set; }

        /// <summary>The draft carries settings that standard mode does not show; the form points at them (SPEC 5.3).</summary>
        public bool AdvancedAdopted { get; set; }
    }
}
