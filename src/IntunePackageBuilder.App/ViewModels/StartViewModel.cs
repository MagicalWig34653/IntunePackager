using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows.Input;
using IntunePackageBuilder.App.Infrastructure;
using IntunePackageBuilder.App.Services;
using IntunePackageBuilder.Build.Workflow;
using IntunePackageBuilder.Core.Logging;
using IntunePackageBuilder.Core.Projects;
using IntunePackageBuilder.Core.Settings;
using IntunePackageBuilder.Core.Sources;
using IntunePackageBuilder.Core.Storage;

namespace IntunePackageBuilder.App.ViewModels
{
    /// <summary>
    /// The start page (SPEC 5.1): usable without any project. Drop area, installer picker, recent projects, the list
    /// of all projects with a search, and the base folder. A bad project or an unreachable base folder never stops the program.
    /// </summary>
    public sealed class StartViewModel : ViewModelBase
    {
        private readonly MainViewModel _main;
        private readonly AppServices _services;
        private readonly List<ProjectItem> _recent = new List<ProjectItem>();
        private readonly List<ProjectItem> _all = new List<ProjectItem>();
        private string _searchText = string.Empty;
        private bool _showAll;
        private string _message;
        private string _baseFolderText;
        private BaseFolderStatus _baseFolderStatus;
        private ProjectItem _selectedProject;

        public StartViewModel(MainViewModel main, AppServices services)
        {
            _main = main;
            _services = services;
            VisibleProjects = new ObservableCollection<ProjectItem>();
            ChooseInstallerCommand = new RelayCommand(ChooseInstaller);
            ChangeBaseFolderCommand = new RelayCommand(ChangeBaseFolder);
            OpenSelectedCommand = new RelayCommand(OpenSelected, () => _selectedProject != null && !_selectedProject.IsBroken);
            OpenProjectFolderCommand = new RelayCommand(OpenProjectFolder);
            Refresh();
        }

        public ObservableCollection<ProjectItem> VisibleProjects { get; private set; }

        public ICommand ChooseInstallerCommand { get; private set; }

        public ICommand ChangeBaseFolderCommand { get; private set; }

        public ICommand OpenSelectedCommand { get; private set; }

        public ICommand OpenProjectFolderCommand { get; private set; }

        /// <summary>The project selected in the list; opening it shows the project view.</summary>
        public ProjectItem SelectedProject
        {
            get { return _selectedProject; }
            set
            {
                if (Set(ref _selectedProject, value))
                {
                    ((RelayCommand)OpenSelectedCommand).RaiseCanExecuteChanged();
                }
            }
        }

        public string SearchText
        {
            get { return _searchText; }
            set
            {
                if (Set(ref _searchText, value ?? string.Empty))
                {
                    UpdateVisibleProjects();
                }
            }
        }

        /// <summary>False: the recently opened projects; true: all projects of the base folder.</summary>
        public bool ShowAll
        {
            get { return _showAll; }
            set
            {
                if (Set(ref _showAll, value))
                {
                    UpdateVisibleProjects();
                }
            }
        }

        public string BaseFolderText
        {
            get { return _baseFolderText; }
            private set { Set(ref _baseFolderText, value); }
        }

        public BaseFolderStatus BaseFolderStatus
        {
            get { return _baseFolderStatus; }
            private set
            {
                if (Set(ref _baseFolderStatus, value))
                {
                    Raise("BaseFolderHasProblem");
                }
            }
        }

        public bool BaseFolderHasProblem
        {
            get { return _baseFolderStatus != BaseFolderStatus.Ok; }
        }

        /// <summary>The last problem with a drop or a choice, as a localized text; null when there is none.</summary>
        public string Message
        {
            get { return _message; }
            private set
            {
                if (Set(ref _message, value))
                {
                    Raise("HasMessage");
                }
            }
        }

        public bool HasMessage
        {
            get { return !string.IsNullOrEmpty(_message); }
        }

        /// <summary>The help text for the drop area; it says whether a folder is accepted.</summary>
        public string DropHint
        {
            get { return Loc.Get(_main.AdvancedMode ? "Start_DropHintAdvanced" : "Start_DropHintStandard"); }
        }

        /// <summary>First start: no project to show at all.</summary>
        public bool IsEmptyState
        {
            get { return _recent.Count == 0 && _all.Count == 0; }
        }

        /// <summary>The list is empty because of the search, not because there are no projects.</summary>
        public bool HasNoMatches
        {
            get { return !IsEmptyState && VisibleProjects.Count == 0; }
        }

        public void OnModeChanged()
        {
            Raise("DropHint");
        }

        /// <summary>Reads the settings and the base folder again and rebuilds the lists. Never throws.</summary>
        public void Refresh()
        {
            var resolution = _services.BaseFolder;
            BaseFolderStatus = resolution.Status;
            BaseFolderText = Loc.Format("BaseFolder_" + resolution.Status, resolution.Path ?? string.Empty);
            Message = _services.SettingsError == null ? null : Loc.Format("Start_SettingsProblem", _services.SettingsError.Message);

            _recent.Clear();
            _all.Clear();
            var projects = new Dictionary<string, ProjectItem>(StringComparer.Ordinal);
            if (resolution.Status == BaseFolderStatus.Ok)
            {
                try
                {
                    foreach (var entry in new ProjectStore(resolution.Path).List())
                    {
                        var item = entry.Project != null
                            ? new ProjectItem { ProjectId = entry.Project.ProjectId, DisplayName = entry.Project.DisplayName, Date = entry.Project.CreatedUtc }
                            : new ProjectItem { ProjectId = entry.FolderName, DisplayName = entry.FolderName + " " + Loc.Get("Start_BrokenProject"), IsBroken = true };
                        _all.Add(item);
                        projects[item.ProjectId] = item;
                    }
                }
                catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
                {
                    _services.Logger.Log(LogLevel.Warning, "The project list could not be read", exception);
                }
            }

            foreach (var recent in _services.Settings.RecentProjects)
            {
                ProjectItem known;
                if (projects.TryGetValue(recent.ProjectId, out known))
                {
                    _recent.Add(new ProjectItem { ProjectId = known.ProjectId, DisplayName = known.DisplayName, Date = recent.LastOpenedUtc, IsBroken = known.IsBroken });
                }
            }

            UpdateVisibleProjects();
            Raise("DropHint");
        }

        /// <summary>A drop on the drop area. Both ways in (drop and picker) end here and are checked the same way (SPEC 5.1, A04).</summary>
        public void HandleDrop(IReadOnlyList<string> paths)
        {
            Message = null;
            try
            {
                var item = SourceImporter.Classify(paths, _main.AdvancedMode);
                string installer = null;
                if (item.Kind == DroppedKind.Folder)
                {
                    installer = AskForInstallerInFolder(item.Path);
                    if (installer == null)
                    {
                        return;
                    }
                }

                _main.ShowForm(SourceAnalyzer.Analyze(item, installer));
            }
            catch (ImportRejectedException exception)
            {
                Message = Texts.ForImport(exception);
            }
            catch (AnalysisFailedException exception)
            {
                _services.Logger.Log(LogLevel.Warning, "The dropped installer could not be analyzed", exception);
                Message = Texts.ForAnalysis(exception);
            }
        }

        private string AskForInstallerInFolder(string folder)
        {
            var chosen = _services.Dialogs.PickFile(Loc.Get("Dialog_InstallerFilter").Split('|')[0], Loc.Get("Dialog_InstallerFilter"), folder);
            if (chosen == null)
            {
                return null;
            }

            var root = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var full = Path.GetFullPath(chosen);
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                Message = Loc.Get("Import_InstallerOutsideSource");
                return null;
            }

            return full.Substring(root.Length);
        }

        private void OpenSelected()
        {
            if (_selectedProject != null && !_selectedProject.IsBroken)
            {
                _main.ShowProject(_selectedProject.ProjectId);
            }
        }

        /// <summary>
        /// Opens an existing project folder (SPEC 5.1). The folder must hold a readable <c>project.json</c> whose ID is the
        /// folder name. A folder outside the base folder is only opened after the user agrees to use its parent as the base folder.
        /// </summary>
        private void OpenProjectFolder()
        {
            Message = null;
            var chosen = _services.Dialogs.PickFolder(Loc.Get("Start_OpenProjectFolder"), _services.BaseFolder.Path);
            if (chosen == null)
            {
                return;
            }

            var full = Path.GetFullPath(chosen).TrimEnd(Path.DirectorySeparatorChar);
            var id = Path.GetFileName(full);
            var parent = Path.GetDirectoryName(full);
            try
            {
                if (parent == null || ProjectId.Check(id) != ProjectIdProblem.None)
                {
                    throw new InvalidProjectIdException(id, ProjectIdProblem.InvalidCharacters);
                }

                new ProjectStore(parent).Load(id);
            }
            catch (Exception exception) when (exception is InvalidProjectIdException || exception is ProjectNotFoundException || exception is StorageFormatException || exception is UnsupportedSchemaException || exception is IOException || exception is UnauthorizedAccessException)
            {
                _services.Logger.Log(LogLevel.Warning, "The chosen folder is not a project", exception);
                Message = Loc.Format("Start_ProjectFolderInvalid", full);
                return;
            }

            var current = _services.BaseFolder;
            var same = current.Status == BaseFolderStatus.Ok
                && string.Equals(Path.GetFullPath(current.Path).TrimEnd(Path.DirectorySeparatorChar), parent.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
            if (!same)
            {
                if (!_services.Dialogs.Confirm(Loc.Get("Start_ProjectFolderBaseTitle"), Loc.Format("Start_ProjectFolderBaseText", id, parent)))
                {
                    return;
                }

                _services.Settings.BaseFolder = parent;
                _services.SaveSettings();
                Refresh();
            }

            _main.ShowProject(id);
        }

        private void ChooseInstaller()
        {
            var path = _services.Dialogs.PickFile(Loc.Get("Dialog_ChooseInstaller"), Loc.Get("Dialog_InstallerFilter"), null);
            if (path != null)
            {
                HandleDrop(new[] { path });
            }
        }

        private void ChangeBaseFolder()
        {
            var current = _services.BaseFolder;
            var chosen = _services.Dialogs.PickFolder(Loc.Get("Dialog_ChooseBaseFolder"), current.Path);
            if (chosen == null)
            {
                return;
            }

            _services.Settings.BaseFolder = chosen;
            _services.SaveSettings();
            Refresh();
        }

        private void UpdateVisibleProjects()
        {
            var source = _showAll ? _all : _recent;
            var term = _searchText.Trim();
            var filtered = source.Where(p => term.Length == 0
                || p.DisplayName.IndexOf(term, StringComparison.CurrentCultureIgnoreCase) >= 0
                || p.ProjectId.IndexOf(term, StringComparison.CurrentCultureIgnoreCase) >= 0);
            VisibleProjects.Clear();
            foreach (var item in filtered)
            {
                VisibleProjects.Add(item);
            }

            Raise("IsEmptyState");
            Raise("HasNoMatches");
        }
    }
}
