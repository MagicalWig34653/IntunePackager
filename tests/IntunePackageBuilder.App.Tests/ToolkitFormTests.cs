using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using IntunePackageBuilder.App.Infrastructure;
using IntunePackageBuilder.App.ViewModels;
using IntunePackageBuilder.Build.Workflow;
using IntunePackageBuilder.Core.Builds;
using IntunePackageBuilder.Core.Projects;
using IntunePackageBuilder.Core.Versions;
using Xunit;

namespace IntunePackageBuilder.App.Tests
{
    public class ToolkitFormTests : IDisposable
    {
        private readonly AppFixture _fixture = new AppFixture();

        public void Dispose()
        {
            _fixture.Dispose();
        }

        private FormViewModel ExeForm()
        {
            var main = new MainViewModel(_fixture.Services);
            var form = new FormViewModel(main, _fixture.Services, _fixture.ExeAnalysis());
            form.InstallArguments.Value = "/quiet /norestart";
            form.UninstallProgram.Value = "C:\\Program Files\\Fabrikam\\uninstall.exe";
            form.UninstallArguments.Value = "/quiet";
            form.DetectionPath.Value = "C:\\Program Files\\Fabrikam\\editor.exe";
            _fixture.Services.Settings.ContentPrepToolPath = _fixture.ToolPath;
            _fixture.Dialogs.Answer = true;
            return form;
        }

        private PackageVersionConfig Configuration(FormViewModel form)
        {
            var problems = new System.Collections.Generic.List<FieldProblem>();
            return form.BuildConfiguration(problems);
        }

        [Fact]
        public void TheBuiltInRuntimeIsTheDefaultAndTheToolkitOptionsStartWithTheirDefaults()
        {
            var form = ExeForm();

            Assert.False(form.Toolkit.UseToolkit);
            Assert.Equal(DeploymentEngine.Native, Configuration(form).Deployment.Engine);
            Assert.Equal("Fluent", form.Toolkit.DialogStyle);
            Assert.True(form.Toolkit.BalloonNotifications);
            Assert.True(form.Toolkit.ShowProgress);
            Assert.False(form.Toolkit.AllowDefer);
            Assert.Equal("3", form.Toolkit.DeferTimes);
            Assert.Equal(string.Empty, form.Toolkit.UiLanguage);
        }

        [Fact]
        public void TheChoicesGoIntoTheConfiguration()
        {
            var form = ExeForm();
            form.Toolkit.UseToolkit = true;
            form.Toolkit.DialogStyle = "Classic";
            form.Toolkit.AccentColor = " #0078D4 ";
            form.Toolkit.CompanyName = "Fabrikam IT";
            form.Toolkit.UiLanguage = "auto";
            form.Toolkit.BalloonNotifications = false;
            form.Toolkit.ShowProgress = false;
            form.Toolkit.AllowDefer = true;
            form.Toolkit.DeferTimes = "5";
            form.Toolkit.CheckDiskSpace = true;
            form.Toolkit.DiskSpaceMb = "2048";
            form.Toolkit.BlockExecution = true;

            var psadt = Configuration(form).Deployment.Psadt;

            Assert.Equal(DeploymentEngine.Psadt, Configuration(form).Deployment.Engine);
            Assert.Equal(PsadtDialogStyle.Classic, psadt.DialogStyle);
            Assert.Equal("#0078D4", psadt.AccentColor);
            Assert.Equal("Fabrikam IT", psadt.CompanyName);
            Assert.Equal("auto", psadt.UiLanguage);
            Assert.False(psadt.BalloonNotifications);
            Assert.False(psadt.ShowProgress);
            Assert.True(psadt.AllowDefer);
            Assert.Equal(5, psadt.DeferTimes);
            Assert.True(psadt.CheckDiskSpace);
            Assert.Equal(2048, psadt.RequiredDiskSpaceMb);
            Assert.True(psadt.BlockExecution);
        }

        [Fact]
        public void NumbersThatAreNoNumbersAreReportedAtTheirEntry()
        {
            var form = ExeForm();
            form.Toolkit.UseToolkit = true;
            form.Toolkit.DeferTimes = "often";
            form.Toolkit.DiskSpaceMb = "lots";
            var problems = new System.Collections.Generic.List<FieldProblem>();

            form.BuildConfiguration(problems);

            Assert.Contains(problems, p => p.Key == "ToolkitDeferTimes");
            Assert.Contains(problems, p => p.Key == "ToolkitDiskSpace");
        }

        [Fact]
        public async Task AnInvalidAccentColourStopsTheBuildAndFocusesItsEntry()
        {
            var form = ExeForm();
            _fixture.UseToolkitZip(true);
            form.Toolkit.UseToolkit = true;
            form.Toolkit.AccentColor = "blue";
            string focused = null;
            form.FocusRequested += (sender, key) => focused = key;

            await form.CreateAsync();

            Assert.Equal("ToolkitAccent", focused);
            Assert.True(form.HasProblems);
            Assert.Equal(0, _fixture.Runner.Calls);
        }

        [Fact]
        public async Task WithoutAToolkitZipNothingIsBuiltAndTheFormSaysWhy()
        {
            var form = ExeForm();
            form.Toolkit.UseToolkit = true;

            await form.CreateAsync();

            Assert.True(form.HasError);
            Assert.Equal(Loc.Get("Build_ToolkitMissing"), form.ErrorText);
            Assert.Equal(0, _fixture.Runner.Calls);
            Assert.Empty(Directory.GetFileSystemEntries(_fixture.BaseFolder));
        }

        [Fact]
        public async Task AKnownToolkitBuildsAndTheVersionIsRecordedInTheManifest()
        {
            var form = ExeForm();
            _fixture.UseToolkitZip(true);
            form.Toolkit.UseToolkit = true;
            BuildOutcome outcome = null;
            form.Completed += (sender, result) => outcome = result;

            await form.CreateAsync();

            Assert.Null(form.ErrorText);
            Assert.NotNull(outcome);
            Assert.DoesNotContain(_fixture.Dialogs.Questions, q => q.Contains(_fixture.ToolkitSha256));
            var manifest = BuildManifestStore.Read(Path.Combine(outcome.BuildDirectory, BuildManifest.FileName));
            Assert.Equal("9.9.9", manifest.ToolkitVersion);
            var stored = new VersionStore(new ProjectStore(_fixture.BaseFolder)).Load(outcome.ProjectId, "12.0.3");
            Assert.Equal(DeploymentEngine.Psadt, stored.Deployment.Engine);
        }

        [Fact]
        public async Task AnUnknownToolkitNeedsAConfirmationEveryTime()
        {
            var form = ExeForm();
            _fixture.UseToolkitZip(false);
            form.Toolkit.UseToolkit = true;
            var completed = 0;
            form.Completed += (sender, result) => completed++;

            _fixture.Dialogs.Answer = false;
            await form.CreateAsync();
            Assert.Equal(0, completed);
            Assert.Equal(0, _fixture.Runner.Calls);
            Assert.Contains(_fixture.Dialogs.Questions, q => q.Contains(_fixture.ToolkitSha256));

            _fixture.Dialogs.Answer = true;
            await form.CreateAsync();
            Assert.Equal(1, completed);
        }

        [Fact]
        public void ChoosingTheZipRemembersItAndShowsWhatIsKnownAboutIt()
        {
            var form = ExeForm();
            _fixture.UseToolkitZip(true);
            _fixture.Services.Settings.PsadtPackagePath = null;
            form.Toolkit.RefreshZipStatus();
            Assert.Equal(Loc.Get("Toolkit_ZipNone"), form.Toolkit.ZipStatus);
            _fixture.Dialogs.FileToPick = _fixture.ToolkitZipPath;

            form.Toolkit.ChooseZipCommand.Execute(null);

            Assert.Equal(_fixture.ToolkitZipPath, _fixture.Services.Settings.PsadtPackagePath);
            Assert.Equal(Loc.Format("Toolkit_ZipKnown", "9.9.9"), form.Toolkit.ZipStatus);
        }

        [Fact]
        public void AZipThatIsNoZipIsShownAsUnusable()
        {
            var form = ExeForm();
            var text = Path.Combine(_fixture.BaseFolder, "x.zip");
            File.WriteAllText(text, "text");
            _fixture.UseToolkitZip(true);
            _fixture.Services.Settings.PsadtPackagePath = text;

            form.Toolkit.RefreshZipStatus();

            Assert.Equal(Loc.Get("Build_ToolkitInvalid"), form.Toolkit.ZipStatus);
        }

        [Fact]
        public async Task APickedImageIsStoredWithTheVersion()
        {
            var form = ExeForm();
            _fixture.UseToolkitZip(true);
            form.Toolkit.UseToolkit = true;
            _fixture.Dialogs.FileToPick = _fixture.WriteImage("my company logo.bin");
            form.Toolkit.Logo.ChooseCommand.Execute(null);
            Assert.True(form.Toolkit.Logo.HasImage);
            Assert.Contains("my company logo.bin", form.Toolkit.Logo.DisplayText);
            BuildOutcome outcome = null;
            form.Completed += (sender, result) => outcome = result;

            await form.CreateAsync();

            Assert.NotNull(outcome);
            var versions = new VersionStore(new ProjectStore(_fixture.BaseFolder));
            Assert.Equal("logo.png", versions.Load(outcome.ProjectId, "12.0.3").Deployment.Psadt.LogoFile);
            Assert.True(File.Exists(Path.Combine(versions.VersionDirectory(outcome.ProjectId, "12.0.3"), "psadt-assets", "logo.png")));
        }

        [Fact]
        public async Task AFileThatIsNoImageStopsTheBuildWithAPlainMessage()
        {
            var form = ExeForm();
            _fixture.UseToolkitZip(true);
            form.Toolkit.UseToolkit = true;
            _fixture.Dialogs.FileToPick = _fixture.WriteImage("fake.png", png: false);
            form.Toolkit.Banner.ChooseCommand.Execute(null);

            await form.CreateAsync();

            Assert.Equal(Loc.Get("Workflow_ToolkitImageInvalid"), form.ErrorText);
            Assert.Equal(0, _fixture.Runner.Calls);
        }

        [Fact]
        public void ClearingAnImageDropsItFromTheConfiguration()
        {
            var form = ExeForm();
            form.Toolkit.UseToolkit = true;
            _fixture.Dialogs.FileToPick = _fixture.WriteImage("a.png");
            form.Toolkit.Logo.ChooseCommand.Execute(null);
            Assert.Equal("logo.png", Configuration(form).Deployment.Psadt.LogoFile);

            form.Toolkit.Logo.ClearCommand.Execute(null);

            Assert.False(form.Toolkit.Logo.HasImage);
            Assert.Null(Configuration(form).Deployment.Psadt.LogoFile);
            Assert.Empty(form.Toolkit.PendingImages);
        }

        [Theory]
        [InlineData("en")]
        [InlineData("de")]
        public void EveryTextOfTheToolkitSectionExistsInBothLanguages(string culture)
        {
            using (var fixture = new AppFixture(culture))
            {
                var main = new MainViewModel(fixture.Services);
                var form = new FormViewModel(main, fixture.Services, fixture.ExeAnalysis());
                var texts = new[]
                {
                    form.Toolkit.ZipStatus, form.Toolkit.Logo.Label, form.Toolkit.LogoDark.Label, form.Toolkit.Banner.Label, form.Toolkit.Logo.DisplayText,
                    form.Toolkit.DialogStyles[0].Text, form.Toolkit.DialogStyles[1].Text, form.Toolkit.Languages[0].Text, form.Toolkit.Languages[1].Text
                };

                Assert.All(texts, text => Assert.False(string.IsNullOrEmpty(text) || text.StartsWith("!", StringComparison.Ordinal), text));
            }
        }

        [Fact]
        public void ATemplateVersionWithTheToolkitStartsWithTheToolkitChosen()
        {
            var main = new MainViewModel(_fixture.Services);
            var draft = _fixture.ExeAnalysis().CreateConfiguration();
            draft.Deployment.Engine = DeploymentEngine.Psadt;
            draft.Deployment.Psadt.AccentColor = "#112233";
            draft.Deployment.Psadt.LogoFile = "logo.png";
            var template = new FormTemplate { Mode = FormMode.Update, ExistingProjectId = "fabrikam-editor", Draft = draft, BasisVersion = "12.0.2" };

            var form = new FormViewModel(main, _fixture.Services, _fixture.ExeAnalysis(), template);

            Assert.True(form.Toolkit.UseToolkit);
            Assert.Equal("#112233", form.Toolkit.AccentColor);
            Assert.Equal("logo.png", form.Toolkit.Logo.StoredFile);
            Assert.Equal("logo.png", Configuration(form).Deployment.Psadt.LogoFile);
        }
    }
}
