using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Input;
using IntunePackageBuilder.App.Infrastructure;
using IntunePackageBuilder.App.Services;
using IntunePackageBuilder.Build.Pipeline;
using IntunePackageBuilder.Core.Builds;
using IntunePackageBuilder.Core.Logging;
using IntunePackageBuilder.Core.Projects;
using IntunePackageBuilder.Core.Settings;
using IntunePackageBuilder.Core.Storage;
using IntunePackageBuilder.Core.Versions;

namespace IntunePackageBuilder.App.ViewModels
{
    /// <summary>A version in the list of the project view.</summary>
    public sealed class VersionItem
    {
        public string VersionText { get; set; }

        public string InstallerText { get; set; }

        public string BuildText { get; set; }

        /// <summary>The version could not be read; it is listed but cannot be used.</summary>
        public bool IsBroken { get; set; }

        /// <summary>The stored configuration; null for a broken version.</summary>
        public PackageVersionConfig Config { get; set; }
    }

    /// <summary>
    /// The project view (SPEC 5.4): name, versions (sorted by numeric version, builds only as additional information) and
    /// free-text notes. Notes are saved explicitly and when the view is left; a failure stays visible and keeps the view open (SPEC 6.2).
    /// </summary>
    public sealed class ProjectViewModel : ViewModelBase
    {
        private readonly MainViewModel _main;
        private readonly AppServices _services;
        private readonly ProjectStore _store;
        private string _notes = string.Empty;
        private string _savedNotes = string.Empty;
        private string _errorText;
        private string _infoText;
        private VersionItem _selectedVersion;

        public ProjectViewModel(MainViewModel main, AppServices services, string projectId)
        {
            _main = main;
            _services = services;
            ProjectId = projectId;
            Versions = new ObservableCollection<VersionItem>();
            SaveNotesCommand = new RelayCommand(() => SaveNotes());
            BackCommand = new RelayCommand(Back);

            var resolution = services.BaseFolder;
            if (resolution.Status != BaseFolderStatus.Ok)
            {
                ErrorText = Loc.Format("Project_LoadFailed", Loc.Format("BaseFolder_" + resolution.Status, resolution.Path ?? string.Empty));
                CanEditNotes = false;
                return;
            }

            _store = new ProjectStore(resolution.Path);
            Load();
        }

        public string ProjectId { get; private set; }

        public string DisplayName { get; private set; }

        public ObservableCollection<VersionItem> Versions { get; private set; }

        public ICommand SaveNotesCommand { get; private set; }

        public ICommand BackCommand { get; private set; }

        /// <summary>False when the project could not be read: the notes are shown empty and are never written then.</summary>
        public bool CanEditNotes { get; private set; } = true;

        public string Notes
        {
            get { return _notes; }
            set
            {
                if (Set(ref _notes, value ?? string.Empty))
                {
                    InfoText = null;
                    Raise("IsDirty");
                }
            }
        }

        public bool IsDirty
        {
            get { return CanEditNotes && !string.Equals(_notes, _savedNotes, StringComparison.Ordinal); }
        }

        public VersionItem SelectedVersion
        {
            get { return _selectedVersion; }
            set { Set(ref _selectedVersion, value); }
        }

        public bool HasVersions
        {
            get { return Versions.Count > 0; }
        }

        public string ErrorText
        {
            get { return _errorText; }
            private set
            {
                if (Set(ref _errorText, value))
                {
                    Raise("HasError");
                }
            }
        }

        public bool HasError
        {
            get { return !string.IsNullOrEmpty(_errorText); }
        }

        public string InfoText
        {
            get { return _infoText; }
            private set
            {
                if (Set(ref _infoText, value))
                {
                    Raise("HasInfo");
                }
            }
        }

        public bool HasInfo
        {
            get { return !string.IsNullOrEmpty(_infoText); }
        }

        /// <summary>Saves the notes when they changed. Returns false when saving failed; the error is then shown.</summary>
        public bool SaveNotes()
        {
            if (!CanEditNotes)
            {
                return true;
            }

            if (!IsDirty)
            {
                return true;
            }

            try
            {
                _store.SaveNotes(ProjectId, _notes);
                _savedNotes = _notes;
                Raise("IsDirty");
                ErrorText = null;
                InfoText = Loc.Get("Project_NotesSaved");
                return true;
            }
            catch (Exception exception) when (IsStorageFailure(exception) || exception is ProjectNotFoundException)
            {
                _services.Logger.Log(LogLevel.Warning, "The project notes could not be saved", exception);
                ErrorText = Loc.Format("Project_NotesError", exception.Message);
                InfoText = null;
                return false;
            }
        }

        private void Back()
        {
            if (SaveNotes())
            {
                _main.ShowStart();
            }
        }

        private void Load()
        {
            try
            {
                var project = _store.Load(ProjectId);
                DisplayName = project.DisplayName;
                _notes = project.Notes ?? string.Empty;
                _savedNotes = _notes;
            }
            catch (Exception exception) when (IsStorageFailure(exception) || exception is ProjectNotFoundException || exception is InvalidProjectIdException)
            {
                _services.Logger.Log(LogLevel.Warning, "The project could not be opened", exception);
                DisplayName = ProjectId;
                CanEditNotes = false;
                ErrorText = Loc.Format("Project_LoadFailed", exception.Message);
                return;
            }

            var versions = new VersionStore(_store);
            try
            {
                foreach (var entry in versions.List(ProjectId))
                {
                    Versions.Add(ToItem(versions, entry));
                }
            }
            catch (Exception exception) when (IsStorageFailure(exception))
            {
                _services.Logger.Log(LogLevel.Warning, "The versions could not be listed", exception);
                ErrorText = Loc.Format("Project_LoadFailed", exception.Message);
            }
        }

        private VersionItem ToItem(VersionStore versions, VersionListEntry entry)
        {
            if (entry.Config == null)
            {
                return new VersionItem { VersionText = entry.FolderName + " " + Loc.Get("Project_VersionBroken"), IsBroken = true, BuildText = string.Empty, InstallerText = string.Empty };
            }

            return new VersionItem
            {
                VersionText = entry.Version.Text,
                InstallerText = entry.Config.Source.InstallerType == InstallerType.Msi ? "MSI" : "EXE",
                BuildText = DescribeBuilds(versions.VersionDirectory(ProjectId, entry.Version.Text)),
                Config = entry.Config
            };
        }

        /// <summary>Counts the published builds of a version (folders named by build ID); the time is only additional information.</summary>
        private static string DescribeBuilds(string versionDirectory)
        {
            var builds = Path.Combine(versionDirectory, BuildPipeline.BuildsFolder);
            string[] ids;
            try
            {
                ids = Directory.Exists(builds)
                    ? Directory.GetDirectories(builds).Select(Path.GetFileName).Where(BuildId.IsValid).OrderBy(n => n, StringComparer.Ordinal).ToArray()
                    : new string[0];
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                ids = new string[0];
            }

            if (ids.Length == 0)
            {
                return Loc.Get("Project_NoBuild");
            }

            var last = ids[ids.Length - 1];
            return ids.Length == 1
                ? Loc.Format("Project_BuildsOne", last)
                : Loc.Format("Project_BuildsMany", ids.Length.ToString(CultureInfo.CurrentCulture), last);
        }

        private static bool IsStorageFailure(Exception exception)
        {
            return exception is StorageFormatException
                || exception is UnsupportedSchemaException
                || exception is MigrationException
                || exception is IOException
                || exception is UnauthorizedAccessException;
        }
    }
}
