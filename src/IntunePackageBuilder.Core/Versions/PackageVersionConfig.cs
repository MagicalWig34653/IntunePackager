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

    /// <summary>Which client runtime runs on the device. Native is the own wrapper; Psadt is the PSAppDeployToolkit.</summary>
    public enum DeploymentEngine
    {
        Native,
        Psadt
    }

    public enum PsadtDialogStyle
    {
        Fluent,
        Classic
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
            Deployment = new DeploymentSection();
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

        /// <summary>Client runtime and its settings. Additive: files written before this section existed read as the native wrapper.</summary>
        public DeploymentSection Deployment { get; set; }

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

    public sealed class DeploymentSection
    {
        public DeploymentSection()
        {
            Engine = DeploymentEngine.Native;
            Psadt = new PsadtSection();
        }

        public DeploymentEngine Engine { get; set; }

        /// <summary>Settings of the PSAppDeployToolkit. Kept when the engine is switched back so the choices are not lost; used only for <see cref="DeploymentEngine.Psadt"/>.</summary>
        public PsadtSection Psadt { get; set; }
    }

    /// <summary>
    /// Options of the PSAppDeployToolkit that the package can be configured with. Images are file names inside the
    /// version's <c>psadt-assets</c> folder, never paths of the user's disk.
    /// </summary>
    public sealed class PsadtSection
    {
        public const int MaxDeferTimes = 99;
        public const int MaxRequiredDiskSpaceMb = 1048576;

        public PsadtSection()
        {
            DialogStyle = PsadtDialogStyle.Fluent;
            BalloonNotifications = true;
            ShowProgress = true;
            DeferTimes = 3;
        }

        public PsadtDialogStyle DialogStyle { get; set; }

        /// <summary>Accent colour of the Fluent dialogs as <c>#RRGGBB</c>, or null for the system colour.</summary>
        public string AccentColor { get; set; }

        /// <summary>Company name shown in the dialogs, or null for the manufacturer of the software.</summary>
        public string CompanyName { get; set; }

        /// <summary>Null follows the language of the generated texts, <c>auto</c> lets the toolkit detect the user's language, otherwise a toolkit language code such as <c>de</c>.</summary>
        public string UiLanguage { get; set; }

        public string LogoFile { get; set; }

        public string LogoDarkFile { get; set; }

        public string BannerFile { get; set; }

        public bool BalloonNotifications { get; set; }

        /// <summary>Shows the progress window while the installer runs.</summary>
        public bool ShowProgress { get; set; }

        /// <summary>Lets the user postpone the installation when programs are running; a postponed run ends with the retry code.</summary>
        public bool AllowDefer { get; set; }

        public int DeferTimes { get; set; }

        public bool CheckDiskSpace { get; set; }

        /// <summary>Required free space in MB; 0 lets the toolkit compute it from the package size.</summary>
        public int RequiredDiskSpaceMb { get; set; }

        /// <summary>Prevents the user from starting the configured programs while the installation runs.</summary>
        public bool BlockExecution { get; set; }

        public bool PromptToSave { get; set; }
    }
}
