using System;
using System.Collections.Generic;
using System.Linq;
using IntunePackageBuilder.Core.Builds;
using IntunePackageBuilder.Core.Versions;

namespace IntunePackageBuilder.Generation.Intune
{
    /// <summary>The configuration of a snapshot is incomplete or invalid; nothing is generated from it.</summary>
    public sealed class InvalidConfigurationException : Exception
    {
        public InvalidConfigurationException(IReadOnlyList<ValidationIssue> issues)
            : base("The configuration is not valid (" + issues.Count + " problem(s)): " + string.Join(", ", issues.Select(i => i.ToString())))
        {
            Issues = issues;
        }

        public IReadOnlyList<ValidationIssue> Issues { get; private set; }
    }

    /// <summary>Values that are not part of the version configuration and are still open decisions.</summary>
    public sealed class IntuneSettingsOptions
    {
        /// <summary>
        /// Minimum Windows version entered as Intune requirement. Open decision (docs/PLANUNG.md section 8):
        /// the default is a placeholder, and the guide says so.
        /// </summary>
        public string MinimumWindowsVersion { get; set; }

        /// <summary>Set to true once the minimum Windows version has been decided; until then the guide marks it as a placeholder.</summary>
        public bool MinimumWindowsVersionConfirmed { get; set; }

        public IntuneSettingsOptions()
        {
            MinimumWindowsVersion = "Windows 10 1607";
        }
    }

    public static class IntuneSettingsBuilder
    {
        /// <summary>The code Intune must classify as a hard reboot: the installer already restarted the device.</summary>
        public const int InstallerRestartedCode = ConfigurationValidator.InstallerRestartedCode;

        public static IntuneSettings From(BuildSnapshot snapshot)
        {
            return From(snapshot, new IntuneSettingsOptions());
        }

        public static IntuneSettings From(BuildSnapshot snapshot, IntuneSettingsOptions options)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException("snapshot");
            }

            if (options == null)
            {
                throw new ArgumentNullException("options");
            }

            var config = snapshot.Configuration;
            var issues = ConfigurationValidator.Validate(config);
            if (issues.Count > 0)
            {
                throw new InvalidConfigurationException(issues);
            }

            var identity = config.Identity;
            var isMsi = config.Source.InstallerType == InstallerType.Msi;

            return new IntuneSettings
            {
                BuildId = snapshot.BuildId,
                Language = snapshot.Language,
                App = new AppSection
                {
                    Type = "Win32",
                    PackageFileName = DeploymentInterface.PackageFileName(config.ProjectId),
                    Name = identity.SoftwareName.Trim(),
                    Publisher = identity.Manufacturer.Trim(),
                    Version = identity.TargetVersion.Trim(),
                    Description = identity.SoftwareName.Trim() + " " + identity.TargetVersion.Trim()
                },
                Program = new ProgramSection
                {
                    InstallCommand = DeploymentInterface.InstallCommand,
                    UninstallCommand = DeploymentInterface.UninstallCommand,
                    InstallContext = "System",
                    TimeoutMinutes = config.Runtime.TimeoutMinutes,
                    RestartBehavior = RestartBehavior.DetermineBasedOnReturnCodes
                },
                Requirements = new RequirementsSection
                {
                    Architecture = "x64",
                    MinimumWindowsVersion = options.MinimumWindowsVersion,
                    MinimumWindowsVersionIsPlaceholder = !options.MinimumWindowsVersionConfirmed
                },
                ReturnCodes = BuildReturnCodes(config.Runtime),
                Detection = new DetectionSection
                {
                    ScriptFileName = DeploymentInterface.DetectionScript,
                    RunAs32Bit = false,
                    EnforceSignatureCheck = false,
                    SignatureStatus = "Unsigned",
                    Rule = new DetectionRuleSection
                    {
                        Method = config.Detection.Method,
                        ProductCode = isMsi ? config.Install.ProductCode.Trim() : null,
                        Path = isMsi ? null : config.Detection.Path.Trim(),
                        MinimumVersion = config.Detection.MinimumVersion.Trim()
                    }
                },
                Logs = new LogsSection
                {
                    Directory = DeploymentInterface.LogDirectory(config.ProjectId),
                    DeploymentLog = DeploymentInterface.DeploymentLogFile,
                    MsiInstallLog = isMsi ? DeploymentInterface.MsiInstallLogFile : null,
                    MsiUninstallLog = isMsi ? DeploymentInterface.MsiUninstallLogFile : null,
                    IntuneManagementExtensionLogs = DeploymentInterface.IntuneManagementExtensionLogs
                },
                ProcessesToClose = config.Interaction.ProcessesToClose
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .Select(p => p.Trim())
                    .ToList()
            };
        }

        /// <summary>
        /// Return codes in ascending order. 1641 is always listed as a hard reboot, so Intune never treats an
        /// installer that restarted the device on its own as a quiet success (SPEC section 8.4).
        /// </summary>
        private static List<ReturnCodeEntry> BuildReturnCodes(RuntimeSection runtime)
        {
            var entries = new List<ReturnCodeEntry>();
            entries.AddRange(runtime.SuccessCodes.Distinct().Select(c => new ReturnCodeEntry { Code = c, Type = ReturnCodeType.Success }));
            entries.AddRange(runtime.RebootCodes.Distinct().Select(c => new ReturnCodeEntry { Code = c, Type = ReturnCodeType.SoftReboot }));
            entries.AddRange(runtime.RetryCodes.Distinct().Select(c => new ReturnCodeEntry { Code = c, Type = ReturnCodeType.Retry }));
            entries.Add(new ReturnCodeEntry { Code = InstallerRestartedCode, Type = ReturnCodeType.HardReboot });
            return entries.OrderBy(e => e.Code).ToList();
        }
    }
}
