using System;
using System.IO;
using System.Linq;
using IntunePackageBuilder.App.ViewModels;
using IntunePackageBuilder.Core.Projects;
using IntunePackageBuilder.Core.Versions;
using Xunit;

namespace IntunePackageBuilder.App.Tests
{
    public class ProjectViewModelTests : IDisposable
    {
        private readonly AppFixture _fixture = new AppFixture();

        public void Dispose()
        {
            _fixture.Dispose();
        }

        private ProjectStore Store()
        {
            return new ProjectStore(_fixture.BaseFolder);
        }

        private void AddVersion(string projectId, string version, InstallerType type = InstallerType.Exe)
        {
            var config = PackageVersionConfig.CreateDefault(projectId, type);
            config.Identity.SoftwareName = "Software " + projectId;
            config.Identity.Manufacturer = "Contoso";
            config.Identity.TargetVersion = version;
            config.Source.InstallerRelativePath = "setup.exe";
            new VersionStore(Store()).Create(projectId, config);
        }

        [Fact]
        public void ThePageShowsTheNameTheNotesAndTheVersionsNewestFirstByNumber()
        {
            var store = Store();
            store.Create("reader", "Contoso Reader");
            store.SaveNotes("reader", "Ask the vendor for the 2.0 installer.");
            AddVersion("reader", "1.9.0");
            AddVersion("reader", "1.10.0");
            AddVersion("reader", "1.2.0");
            var main = new MainViewModel(_fixture.Services);

            main.ShowProject("reader");
            var project = (ProjectViewModel)main.Current;

            Assert.Equal("Contoso Reader", project.DisplayName);
            Assert.Equal("Ask the vendor for the 2.0 installer.", project.Notes);
            Assert.Equal(new[] { "1.10.0", "1.9.0", "1.2.0" }, project.Versions.Select(v => v.VersionText).ToArray());
            Assert.False(project.IsDirty);
        }

        [Fact]
        public void NotesAreSavedExplicitlyAndSurviveReopening()
        {
            Store().Create("reader", "Contoso Reader");
            var main = new MainViewModel(_fixture.Services);
            main.ShowProject("reader");
            var project = (ProjectViewModel)main.Current;

            project.Notes = "Pilot group first.";
            Assert.True(project.IsDirty);
            project.SaveNotesCommand.Execute(null);

            Assert.False(project.IsDirty);
            Assert.True(project.HasInfo);
            Assert.Equal("Pilot group first.", Store().Load("reader").Notes);
        }

        [Fact]
        public void LeavingTheProjectViewSavesChangedNotes()
        {
            Store().Create("reader", "Contoso Reader");
            var main = new MainViewModel(_fixture.Services);
            main.ShowProject("reader");
            ((ProjectViewModel)main.Current).Notes = "Left without pressing save.";

            ((ProjectViewModel)main.Current).BackCommand.Execute(null);

            Assert.IsType<StartViewModel>(main.Current);
            Assert.Equal("Left without pressing save.", Store().Load("reader").Notes);
        }

        [Fact]
        public void ClosingTheWindowSavesChangedNotes()
        {
            Store().Create("reader", "Contoso Reader");
            var main = new MainViewModel(_fixture.Services);
            main.ShowProject("reader");
            ((ProjectViewModel)main.Current).Notes = "Typed just before closing.";

            Assert.True(main.PrepareClose());

            Assert.Equal("Typed just before closing.", Store().Load("reader").Notes);
        }

        [Fact]
        public void AFailureToSaveIsShownAndKeepsThePageOpen()
        {
            Store().Create("reader", "Contoso Reader");
            var main = new MainViewModel(_fixture.Services);
            main.ShowProject("reader");
            var project = (ProjectViewModel)main.Current;
            project.Notes = "These must not be lost silently.";
            Directory.Delete(Path.Combine(_fixture.BaseFolder, "reader"), true);

            project.BackCommand.Execute(null);

            Assert.Same(project, main.Current);
            Assert.True(project.HasError);
            Assert.True(project.IsDirty);
            Assert.False(main.PrepareClose());
        }

        [Fact]
        public void SwitchingBetweenProjectsKeepsEachProjectsOwnData()
        {
            var store = Store();
            store.Create("alpha", "Alpha");
            store.Create("beta", "Beta");
            store.SaveNotes("alpha", "alpha notes");
            store.SaveNotes("beta", "beta notes");
            AddVersion("alpha", "1.0.0");
            AddVersion("beta", "2.0.0");
            var main = new MainViewModel(_fixture.Services);

            main.ShowProject("alpha");
            var first = (ProjectViewModel)main.Current;
            first.Notes = "alpha notes edited";
            first.BackCommand.Execute(null);
            main.ShowProject("beta");
            var second = (ProjectViewModel)main.Current;

            Assert.Equal("beta notes", second.Notes);
            Assert.Equal(new[] { "2.0.0" }, second.Versions.Select(v => v.VersionText).ToArray());
            Assert.Equal("alpha notes edited", store.Load("alpha").Notes);
            Assert.Equal("beta notes", store.Load("beta").Notes);
            Assert.Equal("beta", _fixture.Services.Settings.RecentProjects[0].ProjectId);
            Assert.Equal("alpha", _fixture.Services.Settings.RecentProjects[1].ProjectId);
        }

        [Fact]
        public void ABrokenProjectIsReportedAndItsNotesAreNeverWritten()
        {
            Store().Create("reader", "Contoso Reader");
            File.WriteAllText(Path.Combine(_fixture.BaseFolder, "reader", Project.FileName), "{ not json");
            var main = new MainViewModel(_fixture.Services);

            main.ShowProject("reader");
            var project = (ProjectViewModel)main.Current;

            Assert.True(project.HasError);
            Assert.False(project.CanEditNotes);
            project.Notes = "typed anyway";
            Assert.True(project.SaveNotes());
            Assert.Equal("{ not json", File.ReadAllText(Path.Combine(_fixture.BaseFolder, "reader", Project.FileName)));
        }

        [Fact]
        public void AProjectFolderInsideTheBaseFolderOpensDirectly()
        {
            Store().Create("reader", "Contoso Reader");
            var main = new MainViewModel(_fixture.Services);
            _fixture.Dialogs.FolderToPick = Path.Combine(_fixture.BaseFolder, "reader");

            main.Start.OpenProjectFolderCommand.Execute(null);

            Assert.IsType<ProjectViewModel>(main.Current);
            Assert.Empty(_fixture.Dialogs.Questions);
        }

        [Fact]
        public void AFolderThatIsNotAProjectIsRefusedWithAMessage()
        {
            Directory.CreateDirectory(Path.Combine(_fixture.BaseFolder, "empty-folder"));
            var main = new MainViewModel(_fixture.Services);
            _fixture.Dialogs.FolderToPick = Path.Combine(_fixture.BaseFolder, "empty-folder");

            main.Start.OpenProjectFolderCommand.Execute(null);

            Assert.IsType<StartViewModel>(main.Current);
            Assert.True(main.Start.HasMessage);
            Assert.DoesNotContain("!Start_", main.Start.Message);
        }

        [Fact]
        public void AProjectOutsideTheBaseFolderNeedsAConfirmationBeforeTheBaseFolderChanges()
        {
            var other = Path.Combine(Path.GetDirectoryName(_fixture.BaseFolder), "other-projects");
            Directory.CreateDirectory(other);
            new ProjectStore(other).Create("elsewhere", "Elsewhere");
            var main = new MainViewModel(_fixture.Services);
            _fixture.Dialogs.FolderToPick = Path.Combine(other, "elsewhere");

            _fixture.Dialogs.Answer = false;
            main.Start.OpenProjectFolderCommand.Execute(null);
            Assert.IsType<StartViewModel>(main.Current);
            Assert.Equal(_fixture.BaseFolder, _fixture.Services.Settings.BaseFolder);

            _fixture.Dialogs.Answer = true;
            main.Start.OpenProjectFolderCommand.Execute(null);
            Assert.IsType<ProjectViewModel>(main.Current);
            Assert.Equal(other, _fixture.Services.Settings.BaseFolder);
        }

        [Fact]
        public void ASelectedProjectInTheListOpensTheProjectView()
        {
            Store().Create("reader", "Contoso Reader");
            var main = new MainViewModel(_fixture.Services);
            main.Start.ShowAll = true;

            Assert.False(main.Start.OpenSelectedCommand.CanExecute(null));
            main.Start.SelectedProject = main.Start.VisibleProjects.Single();
            Assert.True(main.Start.OpenSelectedCommand.CanExecute(null));
            main.Start.OpenSelectedCommand.Execute(null);

            Assert.IsType<ProjectViewModel>(main.Current);
        }
    }
}
