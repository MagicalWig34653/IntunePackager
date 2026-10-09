namespace IntunePackageBuilder.Generation.Intune
{
    /// <summary>
    /// Names that connect the package, the Intune settings and the guide: the entry script of the package,
    /// the commands entered in Intune, the detection script and the log files on the device. They are defined
    /// once here, so the guide, the JSON and CSV files and the result page cannot drift apart. The client
    /// runtime templates must provide exactly these files (SPEC section 9).
    /// </summary>
    public static class DeploymentInterface
    {
        public const string EntryScript = "Install.cmd";
        public const string InstallCommand = "Install.cmd";
        public const string UninstallCommand = "Install.cmd -DeploymentType Uninstall";
        public const string DetectionScript = "Detect-App.ps1";

        public const string LogRoot = @"C:\Windows\Logs\Intune\PackageDeploy";
        public const string DeploymentLogFile = "Deployment.log";
        public const string MsiInstallLogFile = "MsiInstall.log";
        public const string MsiUninstallLogFile = "MsiUninstall.log";
        public const string IntuneManagementExtensionLogs = @"C:\ProgramData\Microsoft\IntuneManagementExtension\Logs";

        public const string PackageExtension = ".intunewin";

        /// <summary>Configuration file the client runtime reads; it lies next to <see cref="EntryScript"/> in the package root.</summary>
        public const string WrapperConfigFile = "Deployment.config.json";

        /// <summary>Folder in the package root that holds the stored source files (installer and vendor files).</summary>
        public const string PackageSourceFolder = "Files";

        /// <summary>Names of the files generated into the <c>intune</c> folder of a build (SPEC section 6.1).</summary>
        public const string SettingsJsonFile = "Einstellungen.json";
        public const string SettingsCsvFile = "Einstellungen.csv";
        public const string GuideFile = "Einrichtung.html";

        public static string LogDirectory(string projectId)
        {
            return LogRoot + "\\" + projectId;
        }

        public static string PackageFileName(string projectId)
        {
            return projectId + PackageExtension;
        }
    }
}
