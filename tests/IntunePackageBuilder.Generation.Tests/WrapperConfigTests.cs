using System.Linq;
using IntunePackageBuilder.Core.Storage;
using IntunePackageBuilder.Core.Versions;
using IntunePackageBuilder.Generation.Intune;
using IntunePackageBuilder.Generation.Wrapper;
using Xunit;

namespace IntunePackageBuilder.Generation.Tests
{
    public class WrapperConfigTests
    {
        private static WrapperConfig Build(PackageVersionConfig config, string language = "en")
        {
            var snapshot = Samples.Snapshot(config, language);
            return WrapperConfigBuilder.From(snapshot, IntuneSettingsBuilder.From(snapshot));
        }

        [Fact]
        public void TheConfigurationCarriesTheValuesOfTheSnapshot()
        {
            var wrapper = Build(Samples.Msi(), "de");

            Assert.Equal(WrapperConfig.CurrentSchemaVersion, wrapper.SchemaVersion);
            Assert.Equal(Samples.BuildIdValue, wrapper.BuildId);
            Assert.Equal("de", wrapper.Language);
            Assert.Equal("contoso-reader", wrapper.ProjectId);
            Assert.Equal("Contoso Reader", wrapper.SoftwareName);
            Assert.Equal("Contoso Ltd.", wrapper.Manufacturer);
            Assert.Equal("4.2.1", wrapper.TargetVersion);
            Assert.Equal(InstallerType.Msi, wrapper.Install.InstallerType);
            Assert.Equal("Files\\setup.msi", wrapper.Install.InstallerPath);
            Assert.Equal(Samples.ProductCode, wrapper.Install.ProductCode);
        }

        [Fact]
        public void TheInstallerPathIsRelativeToThePackageRootWithBackslashes()
        {
            var config = Samples.Msi();
            config.Source.InstallerRelativePath = "vendor/bin/setup.msi";

            Assert.Equal("Files\\vendor\\bin\\setup.msi", Build(config).Install.InstallerPath);
        }

        [Fact]
        public void ExeValuesComeFromTheConfigurationAndAreNotInvented()
        {
            var wrapper = Build(Samples.Exe());

            Assert.Equal(InstallerType.Exe, wrapper.Install.InstallerType);
            Assert.Equal("/quiet /norestart", wrapper.Install.Arguments);
            Assert.Equal("C:\\Program Files\\Fabrikam\\uninstall.exe", wrapper.Uninstall.ExecutablePath);
            Assert.Equal("/quiet", wrapper.Uninstall.Arguments);
            Assert.Null(wrapper.Install.ProductCode);
        }

        [Fact]
        public void TimeoutReturnCodesLogsAndDetectionAgreeWithTheIntuneSettings()
        {
            var snapshot = Samples.Snapshot(Samples.Msi());
            var settings = IntuneSettingsBuilder.From(snapshot);
            var wrapper = WrapperConfigBuilder.From(snapshot, settings);

            Assert.Equal(settings.Program.TimeoutMinutes, wrapper.TimeoutMinutes);
            Assert.Equal(
                settings.ReturnCodes.Select(e => e.Code + ":" + e.Type),
                wrapper.ReturnCodes.Select(e => e.Code + ":" + e.Type));
            Assert.Equal(settings.Logs.Directory, wrapper.Logs.Directory);
            Assert.Equal(settings.Logs.DeploymentLog, wrapper.Logs.DeploymentLog);
            Assert.Equal(settings.Logs.MsiInstallLog, wrapper.Logs.MsiInstallLog);
            Assert.Equal(settings.Logs.MsiUninstallLog, wrapper.Logs.MsiUninstallLog);
            Assert.Equal(settings.Detection.Rule.ProductCode, wrapper.Detection.ProductCode);
            Assert.Equal(settings.Detection.Rule.MinimumVersion, wrapper.Detection.MinimumVersion);
        }

        [Fact]
        public void TheInstallerRestartCodeIsAlwaysAHardReboot()
        {
            var entry = Build(Samples.Msi()).ReturnCodes.Single(e => e.Code == 1641);

            Assert.Equal(ReturnCodeType.HardReboot, entry.Type);
        }

        [Fact]
        public void ConfiguredMessagesProcessesAndShortcutsAreCarriedOver()
        {
            var config = Samples.Msi();
            config.Interaction.ProcessesToClose.Add("reader.exe");
            config.Interaction.InstallMessage = " Installing ";
            config.Interaction.DetailMessage = "   ";
            config.PostInstall.SharedShortcutsToRemove.Add(new SharedShortcut { Root = ShortcutRoot.PublicDesktop, RelativePath = "Reader.lnk" });

            var wrapper = Build(config);

            Assert.Equal(new[] { "reader.exe" }, wrapper.ProcessesToClose);
            Assert.Equal("Installing", wrapper.InstallMessage);
            Assert.Null(wrapper.DetailMessage);
            Assert.Equal(ShortcutRoot.PublicDesktop, wrapper.SharedShortcutsToRemove.Single().Root);
            Assert.Equal("Reader.lnk", wrapper.SharedShortcutsToRemove.Single().RelativePath);
        }

        [Fact]
        public void SettingsOfAnotherBuildAreRejected()
        {
            var snapshot = Samples.Snapshot(Samples.Msi());
            var other = IntuneSettingsBuilder.From(snapshot);
            other.BuildId = "20261008-153412-ffff";

            Assert.Throws<System.ArgumentException>(() => WrapperConfigBuilder.From(snapshot, other));
        }

        [Fact]
        public void TheJsonUsesCamelCaseTextEnumsAndOmitsEmptyValues()
        {
            var json = WrapperConfigBuilder.ToJson(Build(Samples.Msi()));
            var document = JsonFormat.ParseObject(json);

            Assert.Equal(1, (int)document["schemaVersion"]);
            Assert.Equal(60, (int)document["timeoutMinutes"]);
            Assert.Equal("Msi", (string)document["install"]["installerType"]);
            Assert.Equal("MsiProductCode", (string)document["detection"]["method"]);
            Assert.Equal("HardReboot", (string)document["returnCodes"].Single(c => (int)c["code"] == 1641)["type"]);
            Assert.Null(document["install"]["arguments"]);
            Assert.Null(document["installMessage"]);
        }

        [Fact]
        public void TheFileIsWrittenAsUtf8WithBom()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ipb-wrapper-" + System.Guid.NewGuid().ToString("N") + ".json");
            try
            {
                WrapperConfigBuilder.WriteTo(path, Build(Samples.Msi()));

                var bytes = System.IO.File.ReadAllBytes(path);
                Assert.True(bytes.Take(3).SequenceEqual(new byte[] { 0xEF, 0xBB, 0xBF }));
            }
            finally
            {
                System.IO.File.Delete(path);
            }
        }
    }
}
