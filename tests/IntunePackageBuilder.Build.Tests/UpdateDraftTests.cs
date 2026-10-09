using System.Linq;
using IntunePackageBuilder.Build.Workflow;
using IntunePackageBuilder.Core.Sources;
using IntunePackageBuilder.Core.Versions;
using Xunit;

namespace IntunePackageBuilder.Build.Tests
{
    public class UpdateDraftTests
    {
        private static PackageVersionConfig ExeBasis()
        {
            var config = PackageVersionConfig.CreateDefault("fabrikam-editor", InstallerType.Exe);
            config.Identity.SoftwareName = "Fabrikam Editor";
            config.Identity.Manufacturer = "Fabrikam";
            config.Identity.TargetVersion = "12.0.3";
            config.Source.InstallerRelativePath = "setup.exe";
            config.Install.Arguments = "/quiet /norestart";
            config.Uninstall.ExecutablePath = "C:\\Program Files\\Fabrikam\\uninstall.exe";
            config.Uninstall.Arguments = "/quiet";
            config.Detection.Path = "C:\\Program Files\\Fabrikam\\editor.exe";
            config.Detection.MinimumVersion = "12.0.3";
            config.Interaction.ProcessesToClose.Add("editor.exe");
            config.Interaction.InstallMessage = "Updating the editor";
            config.PostInstall.SharedShortcutsToRemove.Add(new SharedShortcut { Root = ShortcutRoot.PublicDesktop, RelativePath = "Fabrikam Editor.lnk" });
            config.Runtime.TimeoutMinutes = 90;
            config.Runtime.SuccessCodes = new System.Collections.Generic.List<int> { 0, 3 };
            return config;
        }

        private static SourceAnalysis ExeAnalysis(string version = "12.1.0")
        {
            return new SourceAnalysis
            {
                Item = new DroppedItem(DroppedKind.Exe, "C:\\drop\\setup.exe"),
                InstallerType = InstallerType.Exe,
                ImportKind = ImportKind.SingleFile,
                InstallerRelativePath = "setup.exe",
                SuggestedName = "Fabrikam Editor 12.1",
                SuggestedManufacturer = "Fabrikam Inc.",
                SuggestedVersion = version,
                Notes = new AnalysisNote[0]
            };
        }

        [Fact]
        public void AnExeUpdateAdoptsTheSettingsAndTakesTheNewVersion()
        {
            var basis = ExeBasis();

            var draft = UpdateDraft.Create(ExeAnalysis(), basis);

            Assert.Equal("Fabrikam Editor", draft.Identity.SoftwareName);
            Assert.Equal("Fabrikam", draft.Identity.Manufacturer);
            Assert.Equal("12.1.0", draft.Identity.TargetVersion);
            Assert.Equal("12.1.0", draft.Detection.MinimumVersion);
            Assert.Equal("/quiet /norestart", draft.Install.Arguments);
            Assert.Equal(basis.Uninstall.ExecutablePath, draft.Uninstall.ExecutablePath);
            Assert.Equal(basis.Detection.Path, draft.Detection.Path);
            Assert.Equal(new[] { "editor.exe" }, draft.Interaction.ProcessesToClose.ToArray());
            Assert.Equal(90, draft.Runtime.TimeoutMinutes);
            Assert.Single(draft.PostInstall.SharedShortcutsToRemove);
        }

        [Fact]
        public void TheBaseVersionIsNeverChanged()
        {
            var basis = ExeBasis();

            var draft = UpdateDraft.Create(ExeAnalysis(), basis);
            draft.Interaction.ProcessesToClose.Add("other.exe");
            draft.Runtime.SuccessCodes.Add(99);
            draft.PostInstall.SharedShortcutsToRemove.Clear();

            Assert.Equal(new[] { "editor.exe" }, basis.Interaction.ProcessesToClose.ToArray());
            Assert.Equal(new[] { 0, 3 }, basis.Runtime.SuccessCodes.ToArray());
            Assert.Single(basis.PostInstall.SharedShortcutsToRemove);
            Assert.Equal("12.0.3", basis.Identity.TargetVersion);
        }

        [Fact]
        public void AnMsiUpdateTakesTheNewProductCodeAndNotTheOldOne()
        {
            var basis = PackageVersionConfig.CreateDefault("contoso-reader", InstallerType.Msi);
            basis.Identity.SoftwareName = "Contoso Reader";
            basis.Identity.Manufacturer = "Contoso Ltd.";
            basis.Identity.TargetVersion = "4.2.1";
            basis.Install.ProductCode = "{8F2C41A7-5B3E-4D19-A7C0-3E6D21B94F05}";
            basis.Install.Arguments = "REBOOT=ReallySuppress";
            var fresh = new SourceAnalysis
            {
                Item = new DroppedItem(DroppedKind.Msi, "C:\\drop\\reader.msi"),
                InstallerType = InstallerType.Msi,
                InstallerRelativePath = "reader.msi",
                SuggestedName = "Contoso Reader",
                SuggestedManufacturer = "Contoso Ltd.",
                SuggestedVersion = "4.3.0",
                ProductCode = "{11111111-2222-3333-4444-555555555555}",
                Notes = new AnalysisNote[0]
            };

            var draft = UpdateDraft.Create(fresh, basis);

            Assert.Equal("{11111111-2222-3333-4444-555555555555}", draft.Install.ProductCode);
            Assert.Equal("4.3.0", draft.Identity.TargetVersion);
            Assert.Equal("REBOOT=ReallySuppress", draft.Install.Arguments);
        }

        [Fact]
        public void ChangingTheInstallerTypeIsRefusedInsteadOfAdoptingIncompatibleSettings()
        {
            var fresh = ExeAnalysis();
            fresh.InstallerType = InstallerType.Msi;

            var exception = Assert.Throws<UpdateDraftException>(() => UpdateDraft.Create(fresh, ExeBasis()));

            Assert.Equal(UpdateProblem.InstallerTypeChanged, exception.Problem);
        }

        [Fact]
        public void AdvancedSettingsAreRecognized()
        {
            var plain = PackageVersionConfig.CreateDefault("x", InstallerType.Exe);
            Assert.False(UpdateDraft.HasAdvancedSettings(plain));
            Assert.True(UpdateDraft.HasAdvancedSettings(ExeBasis()));

            var timeout = PackageVersionConfig.CreateDefault("x", InstallerType.Exe);
            timeout.Runtime.TimeoutMinutes = 5;
            Assert.True(UpdateDraft.HasAdvancedSettings(timeout));

            var msiArguments = PackageVersionConfig.CreateDefault("x", InstallerType.Msi);
            msiArguments.Install.Arguments = "ADDLOCAL=All";
            Assert.True(UpdateDraft.HasAdvancedSettings(msiArguments));
        }

        [Fact]
        public void ACloneHasTheSameContentButNoSharedLists()
        {
            var basis = ExeBasis();

            var copy = UpdateDraft.Clone(basis);

            Assert.Equal(basis.Install.Arguments, copy.Install.Arguments);
            Assert.Equal(basis.Runtime.SuccessCodes, copy.Runtime.SuccessCodes);
            Assert.NotSame(basis.Runtime.SuccessCodes, copy.Runtime.SuccessCodes);
            Assert.NotSame(basis.Interaction.ProcessesToClose, copy.Interaction.ProcessesToClose);
            Assert.NotSame(basis.PostInstall.SharedShortcutsToRemove[0], copy.PostInstall.SharedShortcutsToRemove[0]);
        }
    }
}
