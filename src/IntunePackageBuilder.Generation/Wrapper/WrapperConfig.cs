using System.Collections.Generic;
using IntunePackageBuilder.Core.Versions;
using IntunePackageBuilder.Generation.Intune;

namespace IntunePackageBuilder.Generation.Wrapper
{
    /// <summary>
    /// Machine-readable configuration of one build for the client runtime (<c>Deployment.config.json</c> in the
    /// package root). It is rendered from the same snapshot and the same <see cref="IntuneSettings"/> as the guide,
    /// so timeout, return codes, commands and log paths cannot differ between the wrapper and what the
    /// administrator enters in Intune (SPEC section 8.4). The wrapper reads it with Windows PowerShell 5.1.
    /// </summary>
    public sealed class WrapperConfig
    {
        public const int CurrentSchemaVersion = 1;

        public WrapperConfig()
        {
            SchemaVersion = CurrentSchemaVersion;
            ReturnCodes = new List<ReturnCodeEntry>();
            ProcessesToClose = new List<string>();
            SharedShortcutsToRemove = new List<WrapperShortcut>();
        }

        public int SchemaVersion { get; set; }

        public string BuildId { get; set; }

        /// <summary>Language of the messages shown to users on the device (<c>de</c> or <c>en</c>).</summary>
        public string Language { get; set; }

        public string ProjectId { get; set; }

        public string SoftwareName { get; set; }

        public string Manufacturer { get; set; }

        public string TargetVersion { get; set; }

        /// <summary>Which entry script the package runs: the own wrapper or the PSAppDeployToolkit.</summary>
        public DeploymentEngine Engine { get; set; }

        /// <summary>Behavior settings the toolkit entry script reads; present only for <see cref="DeploymentEngine.Psadt"/>. Looks (colours, images, dialog style) live in <c>Config\config.psd1</c>.</summary>
        public WrapperPsadt Psadt { get; set; }

        public WrapperInstall Install { get; set; }

        public WrapperUninstall Uninstall { get; set; }

        /// <summary>The state the wrapper verifies after install; same rule as the detection script.</summary>
        public DetectionRuleSection Detection { get; set; }

        public int TimeoutMinutes { get; set; }

        public List<ReturnCodeEntry> ReturnCodes { get; set; }

        public List<string> ProcessesToClose { get; set; }

        public string InstallMessage { get; set; }

        public string UninstallMessage { get; set; }

        public string DetailMessage { get; set; }

        public List<WrapperShortcut> SharedShortcutsToRemove { get; set; }

        public WrapperLogs Logs { get; set; }
    }

    public sealed class WrapperPsadt
    {
        public bool ShowProgress { get; set; }

        public bool AllowDefer { get; set; }

        public int DeferTimes { get; set; }

        public bool CheckDiskSpace { get; set; }

        public int RequiredDiskSpaceMb { get; set; }
    }

    public sealed class WrapperInstall
    {
        public InstallerType InstallerType { get; set; }

        /// <summary>Path of the installer relative to the package root, for example <c>Files\setup.msi</c>.</summary>
        public string InstallerPath { get; set; }

        /// <summary>Silent arguments of the vendor (EXE) or additional MSI properties; never guessed.</summary>
        public string Arguments { get; set; }

        public string ProductCode { get; set; }

        public TargetArchitecture TargetArchitecture { get; set; }
    }

    public sealed class WrapperUninstall
    {
        public string ProductCode { get; set; }

        public string ExecutablePath { get; set; }

        public string Arguments { get; set; }
    }

    public sealed class WrapperShortcut
    {
        public ShortcutRoot Root { get; set; }

        public string RelativePath { get; set; }
    }

    public sealed class WrapperLogs
    {
        public string Directory { get; set; }

        public string DeploymentLog { get; set; }

        public string MsiInstallLog { get; set; }

        public string MsiUninstallLog { get; set; }
    }
}
