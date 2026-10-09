using System.Linq;
using IntunePackageBuilder.Core.Versions;
using IntunePackageBuilder.Generation.Intune;
using Xunit;

namespace IntunePackageBuilder.Generation.Tests
{
    public class IntuneSettingsTests
    {
        [Fact]
        public void MsiSettings_CarryTheValuesOfTheSnapshot()
        {
            var settings = IntuneSettingsBuilder.From(Samples.Snapshot(Samples.Msi(), "de"));

            Assert.Equal(Samples.BuildIdValue, settings.BuildId);
            Assert.Equal("de", settings.Language);
            Assert.Equal("Win32", settings.App.Type);
            Assert.Equal("contoso-reader.intunewin", settings.App.PackageFileName);
            Assert.Equal("Contoso Reader", settings.App.Name);
            Assert.Equal("Contoso Ltd.", settings.App.Publisher);
            Assert.Equal("4.2.1", settings.App.Version);
            Assert.Equal("Contoso Reader 4.2.1", settings.App.Description);
        }

        [Fact]
        public void TheCommandsAreTheOnesTheSpecificationShows()
        {
            var settings = IntuneSettingsBuilder.From(Samples.Snapshot(Samples.Msi()));

            Assert.Equal("Install.cmd", settings.Program.InstallCommand);
            Assert.Equal("Install.cmd -DeploymentType Uninstall", settings.Program.UninstallCommand);
            Assert.Equal("System", settings.Program.InstallContext);
            Assert.Equal(RestartBehavior.DetermineBasedOnReturnCodes, settings.Program.RestartBehavior);
            Assert.Equal(60, settings.Program.TimeoutMinutes);
        }

        [Fact]
        public void TheTimeoutComesFromTheConfiguration()
        {
            var config = Samples.Msi();
            config.Runtime.TimeoutMinutes = 120;

            Assert.Equal(120, IntuneSettingsBuilder.From(Samples.Snapshot(config)).Program.TimeoutMinutes);
        }

        [Fact]
        public void TheRequirementIsA64BitWindowsFromServer2019OnAndIsNotAPlaceholder()
        {
            var settings = IntuneSettingsBuilder.From(Samples.Snapshot(Samples.Msi()));

            Assert.Equal("x64", settings.Requirements.Architecture);
            Assert.Equal("Windows 10 1809", settings.Requirements.MinimumWindowsVersion);
            Assert.False(settings.Requirements.MinimumWindowsVersionIsPlaceholder);
        }

        [Fact]
        public void TheMinimumWindowsVersionCanBeSetByOptions()
        {
            var options = new IntuneSettingsOptions { MinimumWindowsVersion = "Windows 11 22H2" };

            Assert.Equal("Windows 11 22H2", IntuneSettingsBuilder.From(Samples.Snapshot(Samples.Msi()), options).Requirements.MinimumWindowsVersion);
        }

        [Fact]
        public void MsiDetection_UsesTheProductCodeAndTheNoncommittalSignatureStatus()
        {
            var detection = IntuneSettingsBuilder.From(Samples.Snapshot(Samples.Msi())).Detection;

            Assert.Equal("Detect-App.ps1", detection.ScriptFileName);
            Assert.False(detection.RunAs32Bit);
            Assert.False(detection.EnforceSignatureCheck);
            Assert.Equal("Unsigned", detection.SignatureStatus);
            Assert.Equal(DetectionMethod.MsiProductCode, detection.Rule.Method);
            Assert.Equal(Samples.ProductCode, detection.Rule.ProductCode);
            Assert.Null(detection.Rule.Path);
            Assert.Equal("4.2.1", detection.Rule.MinimumVersion);
        }

        [Fact]
        public void ExeDetection_UsesTheConfiguredFile()
        {
            var detection = IntuneSettingsBuilder.From(Samples.Snapshot(Samples.Exe())).Detection;

            Assert.Equal(DetectionMethod.FileVersion, detection.Rule.Method);
            Assert.Equal("C:\\Program Files\\Fabrikam\\editor.exe", detection.Rule.Path);
            Assert.Null(detection.Rule.ProductCode);
            Assert.Equal("12.0.3", detection.Rule.MinimumVersion);
        }

        [Fact]
        public void TheLogPathsNameTheProjectAndMsiLogsExistOnlyForMsi()
        {
            var msi = IntuneSettingsBuilder.From(Samples.Snapshot(Samples.Msi())).Logs;
            var exe = IntuneSettingsBuilder.From(Samples.Snapshot(Samples.Exe())).Logs;

            Assert.Equal("C:\\Windows\\Logs\\Intune\\PackageDeploy\\contoso-reader", msi.Directory);
            Assert.Equal("Deployment.log", msi.DeploymentLog);
            Assert.Equal("MsiInstall.log", msi.MsiInstallLog);
            Assert.Equal("MsiUninstall.log", msi.MsiUninstallLog);
            Assert.Equal("C:\\ProgramData\\Microsoft\\IntuneManagementExtension\\Logs", msi.IntuneManagementExtensionLogs);
            Assert.Equal("C:\\Windows\\Logs\\Intune\\PackageDeploy\\fabrikam-editor", exe.Directory);
            Assert.Null(exe.MsiInstallLog);
            Assert.Null(exe.MsiUninstallLog);
        }

        [Fact]
        public void TheDefaultReturnCodesFollowTheSpecificationAndListTheInstallerRestartAsAHardReboot()
        {
            var codes = IntuneSettingsBuilder.From(Samples.Snapshot(Samples.Msi())).ReturnCodes;

            Assert.Equal(new[] { 0, 1618, 1641, 3010 }, codes.Select(c => c.Code).ToArray());
            Assert.Equal(
                new[] { ReturnCodeType.Success, ReturnCodeType.Retry, ReturnCodeType.HardReboot, ReturnCodeType.SoftReboot },
                codes.Select(c => c.Type).ToArray());
        }

        [Fact]
        public void ConfiguredVendorCodesAreIncludedWithoutDuplicates()
        {
            var config = Samples.Msi();
            config.Runtime.SuccessCodes.AddRange(new[] { 1707, 0 });
            config.Runtime.RetryCodes.Add(1619);

            var codes = IntuneSettingsBuilder.From(Samples.Snapshot(config)).ReturnCodes;

            Assert.Equal(new[] { 0, 1618, 1619, 1641, 1707, 3010 }, codes.Select(c => c.Code).ToArray());
            Assert.Equal(ReturnCodeType.Success, codes.Single(c => c.Code == 1707).Type);
            Assert.Equal(ReturnCodeType.Retry, codes.Single(c => c.Code == 1619).Type);
        }

        [Theory]
        [InlineData("success")]
        [InlineData("reboot")]
        [InlineData("retry")]
        public void TheInstallerRestartCodeCanNeverBeConfiguredAsAnythingButAHardReboot(string list)
        {
            var config = Samples.Msi();
            if (list == "success")
            {
                config.Runtime.SuccessCodes.Add(1641);
            }
            else if (list == "reboot")
            {
                config.Runtime.RebootCodes.Add(1641);
            }
            else
            {
                config.Runtime.RetryCodes.Add(1641);
            }

            var ex = Assert.Throws<InvalidConfigurationException>(() => IntuneSettingsBuilder.From(Samples.Snapshot(config)));

            Assert.Contains(ex.Issues, i => i.Code == ValidationCode.ForcedRestartCodeNotAllowed);
        }

        [Fact]
        public void AnIncompleteExeConfigurationIsNeverTurnedIntoSettings()
        {
            var config = PackageVersionConfig.CreateDefault("fabrikam-editor", InstallerType.Exe);
            config.Identity.SoftwareName = "Fabrikam Editor";
            config.Identity.Manufacturer = "Fabrikam";
            config.Identity.TargetVersion = "12.0.3";
            config.Source.InstallerRelativePath = "setup.exe";

            var ex = Assert.Throws<InvalidConfigurationException>(() => IntuneSettingsBuilder.From(Samples.Snapshot(config)));

            Assert.Contains(ex.Issues, i => i.Field == "install.arguments");
            Assert.Contains(ex.Issues, i => i.Field == "detection.path");
        }

        [Fact]
        public void ValuesAreTrimmedAndEmptyProcessNamesDropped()
        {
            var config = Samples.Msi();
            config.Identity.SoftwareName = "  Contoso Reader ";
            config.Identity.Manufacturer = " Contoso Ltd. ";
            config.Interaction.ProcessesToClose.AddRange(new[] { " reader.exe ", "", "   ", "helper.exe" });

            var settings = IntuneSettingsBuilder.From(Samples.Snapshot(config));

            Assert.Equal("Contoso Reader", settings.App.Name);
            Assert.Equal("Contoso Ltd.", settings.App.Publisher);
            Assert.Equal(new[] { "reader.exe", "helper.exe" }, settings.ProcessesToClose.ToArray());
        }

        [Fact]
        public void SettingsDoNotChangeWhenTheOriginalConfigurationIsEditedAfterwards()
        {
            var config = Samples.Msi();
            var snapshot = Samples.Snapshot(config);

            config.Identity.SoftwareName = "Renamed";
            config.Identity.TargetVersion = "9.9";
            config.Runtime.TimeoutMinutes = 5;
            var settings = IntuneSettingsBuilder.From(snapshot);

            Assert.Equal("Contoso Reader", settings.App.Name);
            Assert.Equal("4.2.1", settings.App.Version);
            Assert.Equal(60, settings.Program.TimeoutMinutes);
        }

        [Fact]
        public void TwoBuildsOfTheSameVersionCanCarryDifferentBuildIds()
        {
            var first = IntuneSettingsBuilder.From(Core.Builds.BuildSnapshot.Create(Samples.Msi(), Samples.Manifest(), "20261008-153412-a3f9", "en", Samples.Created));
            var second = IntuneSettingsBuilder.From(Core.Builds.BuildSnapshot.Create(Samples.Msi(), Samples.Manifest(), "20261009-080000-0001", "en", Samples.Created));

            Assert.NotEqual(first.BuildId, second.BuildId);
            Assert.Equal(first.App.Version, second.App.Version);
            Assert.Equal(first.App.PackageFileName, second.App.PackageFileName);
        }
    }
}
