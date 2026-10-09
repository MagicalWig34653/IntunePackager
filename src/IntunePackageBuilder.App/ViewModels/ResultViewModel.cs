using System.Windows.Input;
using IntunePackageBuilder.App.Infrastructure;
using IntunePackageBuilder.App.Services;
using IntunePackageBuilder.Build.Workflow;

namespace IntunePackageBuilder.App.ViewModels
{
    /// <summary>The result page after a successful build (SPEC 5.5). Describes the build that just finished and nothing else.</summary>
    public sealed class ResultViewModel : ViewModelBase
    {
        private readonly AppServices _services;
        private bool _copied;

        public ResultViewModel(MainViewModel main, AppServices services, BuildOutcome outcome)
        {
            _services = services;
            Outcome = outcome;
            OpenFolderCommand = new RelayCommand(() => _services.Shell.OpenFolder(Outcome.BuildDirectory));
            OpenGuideCommand = new RelayCommand(() => _services.Shell.OpenFile(Outcome.GuidePath));
            CopyValuesCommand = new RelayCommand(CopyValues);
            NewPackageCommand = new RelayCommand(main.ShowStart);
        }

        public BuildOutcome Outcome { get; private set; }

        public ICommand OpenFolderCommand { get; private set; }

        public ICommand OpenGuideCommand { get; private set; }

        public ICommand CopyValuesCommand { get; private set; }

        public ICommand NewPackageCommand { get; private set; }

        /// <summary>True after the copy; the page then says that the values of this build were copied.</summary>
        public bool Copied
        {
            get { return _copied; }
            private set { Set(ref _copied, value); }
        }

        private void CopyValues()
        {
            _services.Shell.CopyText(Outcome.ClipboardText);
            Copied = true;
        }
    }
}
