using System.Collections.Generic;
using IntunePackageBuilder.Core.Versions;

namespace IntunePackageBuilder.Generation.Intune
{
    public enum ReturnCodeType
    {
        Success,
        SoftReboot,
        HardReboot,
        Retry
    }

    public enum RestartBehavior
    {
        /// <summary>Intune decides from the return codes. The wrapper never forces a restart.</summary>
        DetermineBasedOnReturnCodes
    }

    public sealed class ReturnCodeEntry
    {
        public int Code { get; set; }

        public ReturnCodeType Type { get; set; }
    }

    public sealed class AppSection
    {
        /// <summary>Always <c>Win32</c> (Windows app).</summary>
        public string Type { get; set; }

        public string PackageFileName { get; set; }

        public string Name { get; set; }

        public string Publisher { get; set; }

        public string Version { get; set; }

        public string Description { get; set; }
    }

    public sealed class ProgramSection
    {
        public string InstallCommand { get; set; }

        public string UninstallCommand { get; set; }

        /// <summary>Always <c>System</c>.</summary>
        public string InstallContext { get; set; }

        public int TimeoutMinutes { get; set; }

        public RestartBehavior RestartBehavior { get; set; }
    }

    public sealed class RequirementsSection
    {
        /// <summary>Operating system architecture of the device. The wrapper needs a 64-bit Windows.</summary>
        public string Architecture { get; set; }

        public string MinimumWindowsVersion { get; set; }
    }

    public sealed class DetectionRuleSection
    {
        public DetectionMethod Method { get; set; }

        /// <summary>MSI only.</summary>
        public string ProductCode { get; set; }

        /// <summary>EXE only: file whose version is checked.</summary>
        public string Path { get; set; }

        public string MinimumVersion { get; set; }
    }

    public sealed class DetectionSection
    {
        public string ScriptFileName { get; set; }

        /// <summary>Intune setting "Run script as 32-bit process on 64-bit clients". The script needs the 64-bit view.</summary>
        public bool RunAs32Bit { get; set; }

        public bool EnforceSignatureCheck { get; set; }

        /// <summary>The real signature state of the script. It is <c>Unsigned</c> until a signing step exists.</summary>
        public string SignatureStatus { get; set; }

        public DetectionRuleSection Rule { get; set; }
    }

    public sealed class LogsSection
    {
        public string Directory { get; set; }

        public string DeploymentLog { get; set; }

        /// <summary>MSI packages only.</summary>
        public string MsiInstallLog { get; set; }

        /// <summary>MSI packages only.</summary>
        public string MsiUninstallLog { get; set; }

        public string IntuneManagementExtensionLogs { get; set; }
    }

    /// <summary>
    /// All values an administrator enters in Intune for one build, from one source. The guide, the JSON file,
    /// the CSV file and the result page are rendered from this object, so they always agree with each other and
    /// with the build's snapshot (SPEC section 9). Nothing here is read from a global template.
    /// </summary>
    public sealed class IntuneSettings
    {
        public const int CurrentSchemaVersion = 1;

        public IntuneSettings()
        {
            SchemaVersion = CurrentSchemaVersion;
            ReturnCodes = new List<ReturnCodeEntry>();
            ProcessesToClose = new List<string>();
        }

        public int SchemaVersion { get; set; }

        public string BuildId { get; set; }

        public string Language { get; set; }

        public AppSection App { get; set; }

        public ProgramSection Program { get; set; }

        public RequirementsSection Requirements { get; set; }

        public List<ReturnCodeEntry> ReturnCodes { get; set; }

        public DetectionSection Detection { get; set; }

        public LogsSection Logs { get; set; }

        public List<string> ProcessesToClose { get; set; }
    }
}
