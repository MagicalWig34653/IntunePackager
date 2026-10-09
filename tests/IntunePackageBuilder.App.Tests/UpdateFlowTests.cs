using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using IntunePackageBuilder.App.ViewModels;
using IntunePackageBuilder.Build.Workflow;
using IntunePackageBuilder.Core.Projects;
using IntunePackageBuilder.Core.Sources;
using IntunePackageBuilder.Core.Versions;
using Xunit;

namespace IntunePackageBuilder.App.Tests
{
    public class UpdateFlowTests : IDisposable
    {
        private readonly AppFixture _fixture = new AppFixture();

        public UpdateFlowTests()
        {
            _fixture.Services.Settings.ContentPrepToolPath = _fixture.ToolPath;
            _fixture.Dialogs.Answer = true;
        }

        public void Dispose()
        {
            _fixture.Dispose();
        }

        private static void FillVendorInput(FormViewModel form)
        {
            form.InstallArguments.Value = "/quiet /norestart";
            form.UninstallProgram.Value = "C:\\Program Files\\Fabrikam\\uninstall.exe";
            form.UninstallArguments.Value = "/quiet";
            form.DetectionPath.Value = "C:\\Program Files\\Fabrikam\\editor.exe";
        }

        /// <summary>Builds version 12.0.3 through the form, as a user would, so the project has a stored source and a build.</summary>
        private async Task<MainViewModel> ProjectWithFirstVersion(bool advancedSettings = false)
        {
            var main = new MainViewModel(_fixture.Services);
            main.ShowForm(_fixture.ExeAnalysis());
            var form = (FormViewModel)main.Current;
            FillVendorInput(form);
            if (advancedSettings)
            {
                main.AdvancedMode = true;
                form.ProcessesToClose.Value = "editor.exe";
                main.AdvancedMode = false;
            }

            await form.CreateAsync();
            Assert.IsType<ResultViewModel>(main.Current);
            return main;
        }

        private string SecondInstaller()
        {
            var path = Path.Combine(Path.GetDirectoryName(_fixture.InstallerPath), "setup-new.exe");
            File.WriteAllBytes(path, new byte[] { 0x4D, 0x5A, 9, 9, 9, 9 });
            return path;
        }

        private static string Fingerprint(string folder)
        {
            return SourceManifestBuilder.Build(folder).ComputeFingerprint();
        }

        [Fact]
        public async Task AnUpdateAdoptsTheSettingsAndLeavesTheBaseVersionUntouched()
        {
            var main = await ProjectWithFirstVersion(advancedSettings: true);
            var versions = new VersionStore(new ProjectStore(_fixture.BaseFolder));
            var baseDirectory = versions.VersionDirectory("fabrikam-editor", "12.0.3");
            var sourceBefore = Fingerprint(Path.Combine(baseDirectory, "source"));
            var configBefore = File.ReadAllText(Path.Combine(baseDirectory, PackageVersionConfig.FileName));

            main.ShowProject("fabrikam-editor");
            var project = (ProjectViewModel)main.Current;
            project.SelectedVersion = project.Versions.Single();
            _fixture.Dialogs.FileToPick = SecondInstaller();
            project.UpdateCommand.Execute(null);
            var form = (FormViewModel)main.Current;

            Assert.Equal("/quiet /norestart", form.InstallArguments.Value);
            Assert.Equal("C:\\Program Files\\Fabrikam\\editor.exe", form.DetectionPath.Value);
            Assert.Equal("editor.exe", form.ProcessesToClose.Value);
            Assert.True(form.ShowAdoptedHint);

            form.TargetVersion.Value = "12.1.0";
            await form.CreateAsync();

            var outcome = ((ResultViewModel)main.Current).Outcome;
            Assert.Equal("fabrikam-editor", outcome.ProjectId);
            Assert.False(outcome.ProjectWasCreated);
            Assert.Equal("12.1.0", outcome.TargetVersion);
            Assert.Equal(new[] { "12.1.0", "12.0.3" }, versions.List("fabrikam-editor").Select(e => e.Version.Text).ToArray());
            Assert.Equal(sourceBefore, Fingerprint(Path.Combine(baseDirectory, "source")));
            Assert.Equal(configBefore, File.ReadAllText(Path.Combine(baseDirectory, PackageVersionConfig.FileName)));
        }

        [Fact]
        public async Task AnUpdateWithTheExistingVersionNumberIsRefusedAtTheEntry()
        {
            var main = await ProjectWithFirstVersion();
            main.ShowProject("fabrikam-editor");
            var project = (ProjectViewModel)main.Current;
            project.SelectedVersion = project.Versions.Single();
            _fixture.Dialogs.FileToPick = SecondInstaller();
            project.UpdateCommand.Execute(null);
            var form = (FormViewModel)main.Current;

            form.TargetVersion.Value = "12.0.3";
            PackageVersionConfig config;

            Assert.False(form.Validate(out config));
            Assert.True(form.TargetVersion.HasError);
        }

        [Fact]
        public async Task AnotherInstallerTypeIsRefusedAndNothingIsAdopted()
        {
            var main = await ProjectWithFirstVersion();
            main.ShowProject("fabrikam-editor");
            var project = (ProjectViewModel)main.Current;
            project.SelectedVersion = project.Versions.Single();
            var msi = Path.Combine(Path.GetDirectoryName(_fixture.InstallerPath), "new.msi");
            File.WriteAllBytes(msi, new byte[] { 1, 2, 3 });
            _fixture.Dialogs.FileToPick = msi;

            project.UpdateCommand.Execute(null);

            Assert.Same(project, main.Current);
            Assert.True(project.HasError);
        }

        [Fact]
        public async Task UpdateNeedsASelectedVersion()
        {
            var main = await ProjectWithFirstVersion();
            main.ShowProject("fabrikam-editor");
            var project = (ProjectViewModel)main.Current;
            project.SelectedVersion = null;

            project.UpdateCommand.Execute(null);
            Assert.True(project.HasError);

            project.LoadVersionCommand.Execute(null);
            Assert.Same(project, main.Current);
        }

        [Fact]
        public async Task ANewVersionWithoutTemplateStartsEmptyButInTheSameProject()
        {
            var main = await ProjectWithFirstVersion();
            main.ShowProject("fabrikam-editor");
            var project = (ProjectViewModel)main.Current;
            _fixture.Dialogs.FileToPick = SecondInstaller();

            project.NewVersionCommand.Execute(null);
            var form = (FormViewModel)main.Current;

            Assert.Equal(string.Empty, form.InstallArguments.Value);
            Assert.False(form.ShowAdoptedHint);
            Assert.Equal("fabrikam-editor", form.ProjectId.Value);
            Assert.True(form.ProjectId.IsReadOnly);
        }

        [Fact]
        public async Task LoadingAVersionBuildsItAgainWithANewBuildIdAndKeepsTheVersionNumber()
        {
            var main = await ProjectWithFirstVersion();
            var first = ((ResultViewModel)main.Current).Outcome;
            main.ShowProject("fabrikam-editor");
            var project = (ProjectViewModel)main.Current;
            project.SelectedVersion = project.Versions.Single();

            project.LoadVersionCommand.Execute(null);
            var form = (FormViewModel)main.Current;
            Assert.True(form.TargetVersion.IsReadOnly);
            Assert.Equal("12.0.3", form.TargetVersion.Value);
            await form.CreateAsync();

            var second = ((ResultViewModel)main.Current).Outcome;
            Assert.Equal("12.0.3", second.TargetVersion);
            Assert.NotEqual(first.BuildId, second.BuildId);
            main.ShowProject("fabrikam-editor");
            Assert.Contains("2 ", ((ProjectViewModel)main.Current).Versions.Single().BuildText);
        }

        [Fact]
        public async Task BackFromAProjectFormReturnsToTheProject()
        {
            var main = await ProjectWithFirstVersion();
            main.ShowProject("fabrikam-editor");
            var project = (ProjectViewModel)main.Current;
            project.SelectedVersion = project.Versions.Single();
            project.LoadVersionCommand.Execute(null);

            ((FormViewModel)main.Current).BackCommand.Execute(null);

            Assert.IsType<ProjectViewModel>(main.Current);
        }

        [Fact]
        public async Task TheHintOnAdoptedAdvancedSettingsLeadsToAdvancedMode()
        {
            var main = await ProjectWithFirstVersion(advancedSettings: true);
            main.ShowProject("fabrikam-editor");
            var project = (ProjectViewModel)main.Current;
            project.SelectedVersion = project.Versions.Single();
            _fixture.Dialogs.FileToPick = SecondInstaller();
            project.UpdateCommand.Execute(null);
            var form = (FormViewModel)main.Current;
            Assert.True(form.ShowAdoptedHint);
            Assert.False(form.ProcessesToClose.IsVisible);

            form.ShowAdvancedCommand.Execute(null);

            Assert.True(main.AdvancedMode);
            Assert.False(form.ShowAdoptedHint);
            Assert.True(form.ProcessesToClose.IsVisible);
        }
    }
}
