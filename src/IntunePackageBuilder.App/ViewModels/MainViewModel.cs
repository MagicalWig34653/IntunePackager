using System.ComponentModel;
using IntunePackageBuilder.App.Infrastructure;
using IntunePackageBuilder.App.Services;
using IntunePackageBuilder.Build.Workflow;

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

        public void ShowStart()
        {
            Start.Refresh();
            Current = Start;
        }

        public void ShowForm(SourceAnalysis analysis)
        {
            var form = new FormViewModel(this, Services, analysis);
            form.Completed += (sender, outcome) => ShowResult(outcome);
            form.BackRequested += (sender, args) => ShowStart();
            Current = form;
        }

        public void ShowResult(BuildOutcome outcome)
        {
            Current = new ResultViewModel(this, Services, outcome);
        }
    }
}
