using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Input;
using IntunePackageBuilder.App.Infrastructure;
using IntunePackageBuilder.App.Services;
using IntunePackageBuilder.Build.Psadt;
using IntunePackageBuilder.Core.Logging;
using IntunePackageBuilder.Core.Versions;

namespace IntunePackageBuilder.App.ViewModels
{
    /// <summary>One entry of a list the user chooses from: the value stored in the configuration and the text shown.</summary>
    public sealed class ChoiceItem
    {
        public ChoiceItem(string value, string text)
        {
            Value = value;
            Text = text;
        }

        public string Value { get; private set; }

        public string Text { get; private set; }
    }

    /// <summary>A branding image of the toolkit dialogs: the file stored with the version and the file the user picked just now.</summary>
    public sealed class ToolkitImage : ViewModelBase
    {
        private readonly AppServices _services;
        private string _storedFile;
        private string _pendingPath;

        public ToolkitImage(AppServices services, PsadtImageKind kind, string labelKey, string storedFile)
        {
            _services = services;
            Kind = kind;
            Label = Loc.Get(labelKey);
            _storedFile = storedFile;
            ChooseCommand = new RelayCommand(Choose);
            ClearCommand = new RelayCommand(Clear);
        }

        public PsadtImageKind Kind { get; private set; }

        public string Label { get; private set; }

        public string StoredFile
        {
            get { return _storedFile; }
        }

        /// <summary>The file the user picked; copied into the version when the package is built. Null when nothing new was picked.</summary>
        public string PendingPath
        {
            get { return _pendingPath; }
        }

        public bool HasImage
        {
            get { return _pendingPath != null || !string.IsNullOrWhiteSpace(_storedFile); }
        }

        public string DisplayText
        {
            get
            {
                if (_pendingPath != null)
                {
                    return Loc.Format("Toolkit_ImageNew", System.IO.Path.GetFileName(_pendingPath));
                }

                return string.IsNullOrWhiteSpace(_storedFile) ? Loc.Get("Toolkit_ImageNone") : Loc.Format("Toolkit_ImageStored", _storedFile);
            }
        }

        public ICommand ChooseCommand { get; private set; }

        public ICommand ClearCommand { get; private set; }

        /// <summary>The file name the configuration holds until the build has stored the picked file under its real name.</summary>
        public string ConfigurationFile
        {
            get
            {
                if (_pendingPath == null)
                {
                    return string.IsNullOrWhiteSpace(_storedFile) ? null : _storedFile;
                }

                switch (Kind)
                {
                    case PsadtImageKind.Logo:
                        return "logo.png";
                    case PsadtImageKind.LogoDark:
                        return "logo-dark.png";
                    default:
                        return "banner.png";
                }
            }
        }

        private void Choose()
        {
            var picked = _services.Dialogs.PickFile(Loc.Get("Dialog_ChooseImage"), Loc.Get("Dialog_ImageFilter"), null);
            if (picked != null)
            {
                _pendingPath = picked;
                Changed();
            }
        }

        private void Clear()
        {
            _pendingPath = null;
            _storedFile = null;
            Changed();
        }

        private void Changed()
        {
            Raise("PendingPath");
            Raise("StoredFile");
            Raise("HasImage");
            Raise("DisplayText");
        }
    }

    /// <summary>
    /// The choice between the built-in runtime and the PSAppDeployToolkit and the options of the toolkit (advanced mode). The toolkit
    /// ZIP is the one the user supplied; the settings only remember its path. Nothing here changes a value of the other entries.
    /// </summary>
    public sealed class ToolkitViewModel : ViewModelBase
    {
        public const string ToolkitDownloadUrl = "https://github.com/PSAppDeployToolkit/PSAppDeployToolkit/releases";

        private readonly AppServices _services;
        private bool _useToolkit;
        private string _zipStatus;
        private string _dialogStyle;
        private string _accentColor;
        private string _companyName;
        private string _uiLanguage;
        private bool _balloon;
        private bool _showProgress;
        private bool _allowDefer;
        private string _deferTimes;
        private bool _checkDiskSpace;
        private string _diskSpaceMb;
        private bool _blockExecution;

        public ToolkitViewModel(AppServices services, DeploymentSection draft)
        {
            _services = services;
            var psadt = draft.Psadt;
            _useToolkit = draft.Engine == DeploymentEngine.Psadt;
            _dialogStyle = psadt.DialogStyle.ToString();
            _accentColor = psadt.AccentColor ?? string.Empty;
            _companyName = psadt.CompanyName ?? string.Empty;
            _uiLanguage = psadt.UiLanguage ?? string.Empty;
            _balloon = psadt.BalloonNotifications;
            _showProgress = psadt.ShowProgress;
            _allowDefer = psadt.AllowDefer;
            _deferTimes = psadt.DeferTimes.ToString(CultureInfo.InvariantCulture);
            _checkDiskSpace = psadt.CheckDiskSpace;
            _diskSpaceMb = psadt.RequiredDiskSpaceMb.ToString(CultureInfo.InvariantCulture);
            _blockExecution = psadt.BlockExecution;

            DialogStyles = new[]
            {
                new ChoiceItem(PsadtDialogStyle.Fluent.ToString(), Loc.Get("Toolkit_StyleFluent")),
                new ChoiceItem(PsadtDialogStyle.Classic.ToString(), Loc.Get("Toolkit_StyleClassic"))
            };
            var languages = new List<ChoiceItem>
            {
                new ChoiceItem(string.Empty, Loc.Get("Toolkit_LanguageBuild")),
                new ChoiceItem(ConfigurationValidator.AutomaticLanguage, Loc.Get("Toolkit_LanguageAuto"))
            };
            languages.AddRange(ConfigurationValidator.PsadtLanguages.Select(code => new ChoiceItem(code, code)));
            Languages = languages;

            Logo = new ToolkitImage(services, PsadtImageKind.Logo, "Toolkit_Logo", psadt.LogoFile);
            LogoDark = new ToolkitImage(services, PsadtImageKind.LogoDark, "Toolkit_LogoDark", psadt.LogoDarkFile);
            Banner = new ToolkitImage(services, PsadtImageKind.Banner, "Toolkit_Banner", psadt.BannerFile);
            Images = new[] { Logo, LogoDark, Banner };

            ChooseZipCommand = new RelayCommand(ChooseZip);
            OpenDownloadCommand = new RelayCommand(() => _services.Shell.OpenUrl(ToolkitDownloadUrl));
            RefreshZipStatus();
        }

        public bool UseToolkit
        {
            get { return _useToolkit; }
            set { Set(ref _useToolkit, value); }
        }

        public string ZipPath
        {
            get { return _services.Settings.PsadtPackagePath; }
        }

        /// <summary>What is known about the chosen ZIP: not chosen, a known version, an unknown version or unusable.</summary>
        public string ZipStatus
        {
            get { return _zipStatus; }
            private set { Set(ref _zipStatus, value); }
        }

        public IReadOnlyList<ChoiceItem> DialogStyles { get; private set; }

        public IReadOnlyList<ChoiceItem> Languages { get; private set; }

        public string DialogStyle
        {
            get { return _dialogStyle; }
            set { Set(ref _dialogStyle, value); }
        }

        public string AccentColor
        {
            get { return _accentColor; }
            set { Set(ref _accentColor, value ?? string.Empty); }
        }

        public string CompanyName
        {
            get { return _companyName; }
            set { Set(ref _companyName, value ?? string.Empty); }
        }

        public string UiLanguage
        {
            get { return _uiLanguage; }
            set { Set(ref _uiLanguage, value ?? string.Empty); }
        }

        public ToolkitImage Logo { get; private set; }

        public ToolkitImage LogoDark { get; private set; }

        public ToolkitImage Banner { get; private set; }

        public IReadOnlyList<ToolkitImage> Images { get; private set; }

        public bool BalloonNotifications
        {
            get { return _balloon; }
            set { Set(ref _balloon, value); }
        }

        public bool ShowProgress
        {
            get { return _showProgress; }
            set { Set(ref _showProgress, value); }
        }

        public bool AllowDefer
        {
            get { return _allowDefer; }
            set { Set(ref _allowDefer, value); }
        }

        public string DeferTimes
        {
            get { return _deferTimes; }
            set { Set(ref _deferTimes, value ?? string.Empty); }
        }

        public bool CheckDiskSpace
        {
            get { return _checkDiskSpace; }
            set { Set(ref _checkDiskSpace, value); }
        }

        public string DiskSpaceMb
        {
            get { return _diskSpaceMb; }
            set { Set(ref _diskSpaceMb, value ?? string.Empty); }
        }

        public bool BlockExecution
        {
            get { return _blockExecution; }
            set { Set(ref _blockExecution, value); }
        }

        public ICommand ChooseZipCommand { get; private set; }

        public ICommand OpenDownloadCommand { get; private set; }

        /// <summary>The images the user picked that the build has to copy into the version, by kind.</summary>
        public IDictionary<PsadtImageKind, string> PendingImages
        {
            get
            {
                return Images.Where(i => i.PendingPath != null).ToDictionary(i => i.Kind, i => i.PendingPath);
            }
        }

        /// <summary>Writes the choices into <paramref name="config"/>. Entries that cannot be read as numbers are reported in <paramref name="problems"/>.</summary>
        public void ApplyTo(PackageVersionConfig config, IList<FieldProblem> problems)
        {
            config.Deployment.Engine = _useToolkit ? DeploymentEngine.Psadt : DeploymentEngine.Native;
            var psadt = config.Deployment.Psadt;
            PsadtDialogStyle style;
            psadt.DialogStyle = Enum.TryParse(_dialogStyle, out style) ? style : PsadtDialogStyle.Fluent;
            psadt.AccentColor = NullIfBlank(_accentColor);
            psadt.CompanyName = NullIfBlank(_companyName);
            psadt.UiLanguage = NullIfBlank(_uiLanguage);
            psadt.LogoFile = Logo.ConfigurationFile;
            psadt.LogoDarkFile = LogoDark.ConfigurationFile;
            psadt.BannerFile = Banner.ConfigurationFile;
            psadt.BalloonNotifications = _balloon;
            psadt.ShowProgress = _showProgress;
            psadt.AllowDefer = _allowDefer;
            psadt.CheckDiskSpace = _checkDiskSpace;
            psadt.BlockExecution = _blockExecution;

            int times;
            if (int.TryParse(_deferTimes.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out times))
            {
                psadt.DeferTimes = times;
            }
            else
            {
                problems.Add(new FieldProblem("ToolkitDeferTimes", Loc.Get("Valid_NotANumber")));
            }

            int megabytes;
            if (string.IsNullOrWhiteSpace(_diskSpaceMb))
            {
                psadt.RequiredDiskSpaceMb = 0;
            }
            else if (int.TryParse(_diskSpaceMb.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out megabytes))
            {
                psadt.RequiredDiskSpaceMb = megabytes;
            }
            else
            {
                problems.Add(new FieldProblem("ToolkitDiskSpace", Loc.Get("Valid_NotANumber")));
            }
        }

        public void RefreshZipStatus()
        {
            var path = ZipPath;
            if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path))
            {
                ZipStatus = Loc.Get("Toolkit_ZipNone");
            }
            else
            {
                try
                {
                    var info = PsadtPackage.Inspect(path, _services.PsadtPinPath, true);
                    ZipStatus = info.Version != null ? Loc.Format("Toolkit_ZipKnown", info.Version) : Loc.Format("Toolkit_ZipUnknown", info.Sha256);
                }
                catch (PsadtException exception)
                {
                    _services.Logger.Log(LogLevel.Warning, "The toolkit ZIP cannot be used", exception);
                    ZipStatus = Loc.Get("Build_ToolkitInvalid");
                }
            }

            Raise("ZipPath");
        }

        private void ChooseZip()
        {
            var chosen = _services.Dialogs.PickFile(Loc.Get("Dialog_ChooseToolkit"), Loc.Get("Dialog_ToolkitFilter"), null);
            if (chosen == null)
            {
                return;
            }

            _services.Settings.PsadtPackagePath = chosen;
            _services.SaveSettings();
            RefreshZipStatus();
        }

        private static string NullIfBlank(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
    }
}
