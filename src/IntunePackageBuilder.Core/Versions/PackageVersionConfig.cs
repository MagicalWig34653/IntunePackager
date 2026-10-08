using System.Collections.Generic;

namespace IntunePackageBuilder.Core.Versions
{
    public enum InstallerType
    {
        Msi,
        Exe
    }

    public enum ImportKind
    {
        SingleFile,
        Folder
    }

    public enum TargetArchitecture
    {
        X64,
        X86
    }

    public enum DetectionMethod
    {
        MsiProductCode,
        FileVersion
    }

    public enum ShortcutRoot
    {
        PublicDesktop,
        CommonStartMenu
    }

    /// <summary>
    /// Content of <c>configuration.json</c> of one software version (SPEC section 6.3).
    /// Values the tool cannot know (EXE switches, uninstall path, detection file) are left empty
    /// and reported by <see cref="ConfigurationValidator"/>; they are never filled with guesses.
    /// </summary>
    public sealed class PackageVersionConfig
    {
        public const int CurrentSchemaVersion = 1;
        public const string FileName = "configuration.json";

        public PackageVersionConfig()
        {
            Identity = new IdentitySection();
            Source = new SourceSection();
            Install = new InstallSection();
            Uninstall = new UninstallSection();
            Detection = new DetectionSection();
            Interaction = new InteractionSection();
            PostInstall = new PostInstallSection();
            Runtime = new RuntimeSection();
        }

        public int SchemaVersion { get; set; }

        public string ProjectId { get; set; }

        public IdentitySection Identity { get; set; }

        public SourceSection Source { get; set; }

        public InstallSection Install { get; set; }

        public UninstallSection Uninstall { get; set; }

        public DetectionSection Detection { get; set; }

        public InteractionSection Interaction { get; set; }

        public PostInstallSection PostInstall { get; set; }

        public RuntimeSection Runtime { get; set; }

        /// <summary>Creates an empty configuration with the standard runtime defaults for the installer type.</summary>
        public static PackageVersionConfig CreateDefault(string projectId, InstallerType type)
        {
            var config = new PackageVersionConfig
            {
                SchemaVersion = CurrentSchemaVersion,
                ProjectId = projectId
            };
            config.Source.InstallerType = type;
            config.Detection.Method = type == InstallerType.Msi ? DetectionMethod.MsiProductCode : DetectionMethod.FileVersion;
            return config;
        }
    }

    public sealed class IdentitySection
    {
        public string SoftwareName { get; set; }

        public string Manufacturer { get; set; }

        /// <summary>Target state on the device. Not the build ID and not the version of this tool.</summary>
        public string TargetVersion { get; set; }
    }

    public sealed class SourceSection
    {
        public InstallerType InstallerType { get; set; }

        /// <summary>Path of the installer relative to the stored source folder.</summary>
        public string InstallerRelativePath { get; set; }

        public ImportKind ImportKind { get; set; }
    }

    public sealed class InstallSection
    {
        /// <summary>Silent install arguments. Required for EXE, vendor supplied.</summary>
        public string Arguments { get; set; }

        public string ProductCode { get; set; }

        public TargetArchitecture TargetArchitecture { get; set; }
    }

    public sealed class UninstallSection
    {
        /// <summary>MSI only; EXE uses <see cref="ExecutablePath"/>.</summary>
        public string ProductCode { get; set; }

        public string ExecutablePath { get; set; }

        public string Arguments { get; set; }
    }

    public sealed class DetectionSection
    {
        public DetectionMethod Method { get; set; }

        /// <summary>EXE only: absolute path (or one starting with an environment variable) of a file with version info.</summary>
        public string Path { get; set; }

        /// <summary>Installed version must be at least this version.</summary>
        public string MinimumVersion { get; set; }
    }

    public sealed class InteractionSection
    {
        public InteractionSection()
        {
            ProcessesToClose = new List<string>();
        }

        public List<string> ProcessesToClose { get; set; }

        public string InstallMessage { get; set; }

        public string UninstallMessage { get; set; }

        public string DetailMessage { get; set; }
    }

    public sealed class SharedShortcut
    {
        public ShortcutRoot Root { get; set; }

        /// <summary>Path of one .lnk file below the root.</summary>
        public string RelativePath { get; set; }
    }

    public sealed class PostInstallSection
    {
        public PostInstallSection()
        {
            SharedShortcutsToRemove = new List<SharedShortcut>();
        }

        public List<SharedShortcut> SharedShortcutsToRemove { get; set; }
    }

    public sealed class RuntimeSection
    {
        public RuntimeSection()
        {
            TimeoutMinutes = 60;
            SuccessCodes = new List<int> { 0 };
            RebootCodes = new List<int> { 3010 };
            RetryCodes = new List<int> { 1618 };
        }

        public int TimeoutMinutes { get; set; }

        public List<int> SuccessCodes { get; set; }

        public List<int> RebootCodes { get; set; }

        public List<int> RetryCodes { get; set; }
    }
}
