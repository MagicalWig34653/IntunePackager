using System.ComponentModel;
using IntunePackageBuilder.App.Infrastructure;
using IntunePackageBuilder.App.Services;
using IntunePackageBuilder.Build.Workflow;
using IntunePackageBuilder.Core.Logging;
using IntunePackageBuilder.Core.Projects;

namespace IntunePackageBuilder.App.ViewModels
{
    /// <summary>
    /// The window: which page is shown and the program mode. The standard mode is active at every start; the mode is
    /// never saved (SPEC 5.2), and switching it changes no value of a form (SPEC 5.3).
    /// </summary>
    public sealed class MainViewModel : ViewModelBase
    {
        private bool _advancedMode;
        private ViewModelBase _current;

        public MainViewModel(AppServices services)
        {
            Services = services;
            Start = new StartViewModel(this, services);
            _current = Start;
        }

        public AppServices Services { get; private set; }

        public string ModeText
        {
            get { return Loc.Get(_advancedMode ? "Mode_Advanced" : "Mode_Standard"); }
        }

        public StartViewModel Start { get; private set; }

        public bool AdvancedMode
        {
            get { return _advancedMode; }
            set
            {
                if (Set(ref _advancedMode, value))
                {
                    Raise("ModeText");
                    Start.OnModeChanged();
                }
            }
        }

        /// <summary>The page in the window: start page, form (also while building) or result.</summary>
        public ViewModelBase Current
        {
            get { return _current; }
            private set { Set(ref _current, value); }
        }

        /// <summary>False while a build runs: closing then needs a confirmation (SPEC 7.2).</summary>
        public bool CanCloseWithoutAsking
        {
            get
            {
                var form = _current as FormViewModel;
                return form == null || !form.IsBusy;
            }
        }

        /// <summary>
        /// Called when the window is about to close: unsaved project notes are saved first (SPEC 6.2). Returns false when
        /// saving failed; the error stays visible in the project view and the window stays open.
        /// </summary>
        public bool PrepareClose()
        {
            var project = _current as ProjectViewModel;
            return project == null || project.SaveNotes();
        }

        public void ShowProject(string projectId)
        {
            Current = new ProjectViewModel(this, Services, projectId);
            try
            {
                Services.Settings.MarkOpened(projectId, Services.Clock());
                Services.SaveSettings();
            }
            catch (InvalidProjectIdException exception)
            {
                Services.Logger.Log(LogLevel.Warning, "The project could not be added to the recent list", exception);
            }
        }

        public void ShowStart()
        {
            Start.Refresh();
            Current = Start;
        }

        public void ShowForm(SourceAnalysis analysis, FormTemplate template = null)
        {
            var form = new FormViewModel(this, Services, analysis, template);
            var projectId = template == null ? null : template.ExistingProjectId;
            form.Completed += (sender, outcome) => ShowResult(outcome);
            form.BackRequested += (sender, args) =>
            {
                // A form that belongs to a project goes back to that project, a new package back to the start page.
                if (projectId != null)
                {
                    ShowProject(projectId);
                }
                else
                {
                    ShowStart();
                }
            };
            Current = form;
        }

        public void ShowResult(BuildOutcome outcome)
        {
            Current = new ResultViewModel(this, Services, outcome);
        }
    }
}
