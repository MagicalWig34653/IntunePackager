using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using IntunePackageBuilder.App.ViewModels;
using IntunePackageBuilder.Build.Packaging;
using IntunePackageBuilder.Build.Workflow;
using IntunePackageBuilder.Core.Versions;
using Xunit;

namespace IntunePackageBuilder.App.Tests
{
    public class FormViewModelTests : IDisposable
    {
        private readonly AppFixture _fixture = new AppFixture();

        public void Dispose()
        {
            _fixture.Dispose();
        }

        private FormViewModel ExeForm(out MainViewModel main)
        {
            main = new MainViewModel(_fixture.Services);
            return new FormViewModel(main, _fixture.Services, _fixture.ExeAnalysis());
        }

        private static void FillVendorInput(FormViewModel form)
        {
            form.InstallArguments.Value = "/quiet /norestart";
            form.UninstallProgram.Value = "C:\\Program Files\\Fabrikam\\uninstall.exe";
            form.UninstallArguments.Value = "/quiet";
            form.DetectionPath.Value = "C:\\Program Files\\Fabrikam\\editor.exe";
        }

        [Fact]
        public void TheFormStartsWithTheValuesOfTheAnalysis()
        {
            MainViewModel main;
            var form = ExeForm(out main);

            Assert.Equal("Fabrikam Editor", form.SoftwareName.Value);
            Assert.Equal("Fabrikam", form.Manufacturer.Value);
            Assert.Equal("12.0.3", form.TargetVersion.Value);
            Assert.Equal(string.Empty, form.InstallArguments.Value);
            Assert.Equal(string.Empty, form.UninstallProgram.Value);
        }

        [Fact]
        public void AnExeAsksForTheVendorInputAndStandardModeHidesTheRest()
        {
            MainViewModel main;
            var form = ExeForm(out main);

            Assert.True(form.InstallArguments.IsVisible);
            Assert.True(form.UninstallProgram.IsVisible);
            Assert.True(form.DetectionPath.IsVisible);
            Assert.False(form.ProductCode.IsVisible);
            Assert.False(form.Timeout.IsVisible);
            Assert.False(form.ProjectId.IsVisible);
        }

        [Fact]
        public void AnMsiNeedsNoTechnicalInputInStandardMode()
        {
            var main = new MainViewModel(_fixture.Services);
            var form = new FormViewModel(main, _fixture.Services, _fixture.MsiAnalysis());

            PackageVersionConfig config;
            Assert.True(form.Validate(out config));
            Assert.False(form.InstallArguments.IsVisible);
            Assert.True(form.ProductCode.IsVisible);
            Assert.True(form.ProductCode.IsReadOnly);
            Assert.Equal("{8F2C41A7-5B3E-4D19-A7C0-3E6D21B94F05}", config.Install.ProductCode);
        }

        [Fact]
        public void MissingVendorInputIsReportedAtTheEntriesAndNeverGuessed()
        {
            MainViewModel main;
            var form = ExeForm(out main);
            string focused = null;
            form.FocusRequested += (sender, key) => focused = key;

            PackageVersionConfig config;
            var valid = form.Validate(out config);

            Assert.False(valid);
            Assert.True(form.InstallArguments.HasError);
            Assert.True(form.UninstallProgram.HasError);
            Assert.True(form.DetectionPath.HasError);
            Assert.False(form.SoftwareName.HasError);
            Assert.Equal("InstallArguments", focused);
            Assert.Null(config.Install.Arguments);
        }

        [Fact]
        public void EditingAnEntryClearsItsProblem()
        {
            MainViewModel main;
            var form = ExeForm(out main);
            PackageVersionConfig config;
            form.Validate(out config);

            form.InstallArguments.Value = "/S";

            Assert.False(form.InstallArguments.HasError);
        }

        [Fact]
        public void AdvancedModeShowsMoreEntriesAndChangesNoValue()
        {
            MainViewModel main;
            var form = ExeForm(out main);
            form.InstallArguments.Value = "/S";
            form.Manufacturer.Value = "Fabrikam GmbH";

            main.AdvancedMode = true;

            Assert.True(form.Timeout.IsVisible);
            Assert.True(form.SuccessCodes.IsVisible);
            Assert.True(form.ProjectId.IsVisible);
            Assert.Equal("/S", form.InstallArguments.Value);
            Assert.Equal("Fabrikam GmbH", form.Manufacturer.Value);

            main.AdvancedMode = false;

            Assert.False(form.Timeout.IsVisible);
            Assert.Equal("/S", form.InstallArguments.Value);
        }

        [Fact]
        public void TheMinimumVersionFollowsTheTargetVersionUntilItIsEditedByHand()
        {
            MainViewModel main;
            var form = ExeForm(out main);

            form.TargetVersion.Value = "12.0.4";
            Assert.Equal("12.0.4", form.DetectionMinimumVersion.Value);

            form.DetectionMinimumVersion.Value = "12.0.0";
            form.TargetVersion.Value = "12.0.5";
            Assert.Equal("12.0.0", form.DetectionMinimumVersion.Value);
        }

        [Fact]
        public void TheProjectIdFollowsTheNameUntilItIsEditedByHand()
        {
            MainViewModel main;
            var form = ExeForm(out main);
            Assert.Equal("fabrikam-editor", form.ProjectId.Value);

            form.SoftwareName.Value = "Fabrikam Studio";
            Assert.Equal("fabrikam-studio", form.ProjectId.Value);

            form.ProjectId.Value = "my-editor";
            form.SoftwareName.Value = "Fabrikam Other";
            Assert.Equal("my-editor", form.ProjectId.Value);
        }

        [Fact]
        public void NumbersAndCodeListsThatCannotBeReadAreReportedAtTheEntry()
        {
            MainViewModel main;
            var form = ExeForm(out main);
            FillVendorInput(form);
            main.AdvancedMode = true;
            form.Timeout.Value = "soon";
            form.RebootCodes.Value = "3010, x";

            PackageVersionConfig config;
            Assert.False(form.Validate(out config));

            Assert.True(form.Timeout.HasError);
            Assert.True(form.RebootCodes.HasError);
        }

        [Fact]
        public void ACodeThatForcesARestartIsRejectedAtTheEntry()
        {
            MainViewModel main;
            var form = ExeForm(out main);
            FillVendorInput(form);
            main.AdvancedMode = true;
            form.SuccessCodes.Value = "0, 1641";

            PackageVersionConfig config;
            Assert.False(form.Validate(out config));

            Assert.True(form.SuccessCodes.HasError);
        }

        [Fact]
        public void ShortcutsAreReadFromLinesWithRootAndPath()
        {
            MainViewModel main;
            var form = ExeForm(out main);
            FillVendorInput(form);
            main.AdvancedMode = true;
            form.Shortcuts.Value = "PublicDesktop|Fabrikam Editor.lnk\r\nCommonStartMenu|Fabrikam\\Editor.lnk";

            PackageVersionConfig config;
            Assert.True(form.Validate(out config));

            Assert.Equal(2, config.PostInstall.SharedShortcutsToRemove.Count);
            Assert.Equal(ShortcutRoot.CommonStartMenu, config.PostInstall.SharedShortcutsToRemove[1].Root);
        }

        [Fact]
        public async Task WithoutTheToolNothingIsBuiltAndTheFormAsksForIt()
        {
            MainViewModel main;
            var form = ExeForm(out main);
            FillVendorInput(form);

            await form.CreateAsync();

            Assert.True(form.NeedsTool);
            Assert.Equal(0, _fixture.Runner.Calls);
            Assert.False(form.IsBusy);
        }

        [Fact]
        public async Task AnUnknownToolVersionNeedsAConfirmationAndIsNotStored()
        {
            MainViewModel main;
            var form = ExeForm(out main);
            FillVendorInput(form);
            _fixture.Services.Settings.ContentPrepToolPath = _fixture.ToolPath;
            _fixture.Dialogs.Answer = false;

            await form.CreateAsync();

            Assert.Single(_fixture.Dialogs.Questions);
            Assert.Equal(0, _fixture.Runner.Calls);
        }

        [Fact]
        public async Task ABuildCreatesTheResultPageAndRemembersTheProject()
        {
            MainViewModel main;
            var form = ExeForm(out main);
            FillVendorInput(form);
            _fixture.Services.Settings.ContentPrepToolPath = _fixture.ToolPath;
            _fixture.Dialogs.Answer = true;
            BuildOutcome outcome = null;
            form.Completed += (sender, result) => outcome = result;

            await form.CreateAsync();

            Assert.Null(form.ErrorText);
            Assert.NotNull(outcome);
            Assert.Equal("fabrikam-editor", outcome.ProjectId);
            Assert.True(File.Exists(outcome.PackagePath));
            Assert.Equal("fabrikam-editor", _fixture.Services.Settings.RecentProjects[0].ProjectId);
            Assert.False(form.IsBusy);
        }

        [Fact]
        public async Task AFailedBuildShowsAnErrorAndNeverTheResultPage()
        {
            MainViewModel main;
            var form = ExeForm(out main);
            FillVendorInput(form);
            _fixture.Services.Settings.ContentPrepToolPath = _fixture.ToolPath;
            _fixture.Dialogs.Answer = true;
            _fixture.Runner.FailWith = new ContentPrepException(ContentPrepProblem.ToolFailed, "exit code 1");
            var completed = false;
            form.Completed += (sender, result) => completed = true;

            await form.CreateAsync();

            Assert.False(completed);
            Assert.True(form.HasError);
            Assert.False(form.IsBusy);
            Assert.DoesNotContain("!Build_", form.ErrorText);
            Assert.DoesNotContain("!ContentPrep_", form.ErrorText);
        }

        [Fact]
        public async Task AnUnreachableBaseFolderIsNeverReplacedAndStopsTheBuild()
        {
            MainViewModel main;
            var form = ExeForm(out main);
            FillVendorInput(form);
            _fixture.Services.Settings.ContentPrepToolPath = _fixture.ToolPath;
            _fixture.Dialogs.Answer = true;
            var missing = Path.Combine(_fixture.BaseFolder, "gone", "share");
            _fixture.Services.Settings.BaseFolder = missing;

            await form.CreateAsync();

            Assert.True(form.HasError);
            Assert.Equal(missing, _fixture.Services.Settings.BaseFolder);
            Assert.Equal(0, _fixture.Runner.Calls);
        }

        [Fact]
        public void BackReturnsToTheStartPage()
        {
            MainViewModel main;
            var form = ExeForm(out main);
            var raised = false;
            form.BackRequested += (sender, args) => raised = true;

            form.BackCommand.Execute(null);

            Assert.True(raised);
        }

        [Fact]
        public void NotesOfTheAnalysisAreShownAsTexts()
        {
            var main = new MainViewModel(_fixture.Services);
            var analysis = _fixture.MsiAnalysis();
            analysis.Notes = new[] { AnalysisNote.MsiPerUserDefault };

            var form = new FormViewModel(main, _fixture.Services, analysis);

            Assert.Single(form.Notes);
            Assert.DoesNotContain("!Note_", form.Notes[0]);
        }
    }
}
