using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using IntunePackageBuilder.App.Infrastructure;
using IntunePackageBuilder.App.Services;
using IntunePackageBuilder.Build.Packaging;
using IntunePackageBuilder.Build.Pipeline;
using IntunePackageBuilder.Build.Workflow;
using IntunePackageBuilder.Core.Logging;
using IntunePackageBuilder.Core.Projects;
using IntunePackageBuilder.Core.Settings;
using IntunePackageBuilder.Core.Sources;
using IntunePackageBuilder.Core.Versions;

namespace IntunePackageBuilder.App.ViewModels
{
    /// <summary>A problem at one entry of the form.</summary>
    public sealed class FieldProblem
    {
        public FieldProblem(string key, string message)
        {
            Key = key;
            Message = message;
        }

        public string Key { get; private set; }

        public string Message { get; private set; }
    }

    /// <summary>
    /// The form for one package (SPEC 5.2 and 5.3) and the build that follows it. Only the entries that fit the installer
    /// are shown; values the tool cannot know stay empty and are asked for. Switching to advanced mode shows more entries
    /// and changes no value. While a build runs the entries are locked.
    /// </summary>
    public sealed class FormViewModel : ViewModelBase
    {
        public const string ToolDownloadUrl = "https://github.com/microsoft/Microsoft-Win32-Content-Prep-Tool";

        private static readonly string[] FieldOrder =
        {
            "SoftwareName", "Manufacturer", "TargetVersion", "ProductCode", "InstallArguments", "UninstallProgram",
            "UninstallArguments", "DetectionPath", "DetectionMinimumVersion", "ProjectId", "ProcessesToClose", "Shortcuts",
            "Timeout", "SuccessCodes", "RebootCodes", "RetryCodes", "InstallMessage", "UninstallMessage", "DetailMessage"
        };

        private readonly MainViewModel _main;
        private readonly AppServices _services;
        private readonly SourceAnalysis _analysis;
        private readonly FormTemplate _template;
        private bool _minimumFollowsTarget = true;
        private bool _isBusy;
        private bool _needsTool;
        private string _phaseText;
        private string _elapsedText;
        private string _errorText;
        private string _sourceCheckText;
        private DateTime _started;

        public FormViewModel(MainViewModel main, AppServices services, SourceAnalysis analysis, FormTemplate template = null)
        {
            _main = main;
            _services = services;
            _analysis = analysis;
            _template = template ?? new FormTemplate { Mode = FormMode.NewPackage };
            var draft = NewDraft();

            SoftwareName = new FormField("SoftwareName");
            Manufacturer = new FormField("Manufacturer");
            TargetVersion = new FormField("TargetVersion");
            ProductCode = new FormField("ProductCode") { IsReadOnly = true };
            InstallArguments = new FormField("InstallArguments");
            UninstallProgram = new FormField("UninstallProgram");
            UninstallArguments = new FormField("UninstallArguments");
            DetectionPath = new FormField("DetectionPath");
            DetectionMinimumVersion = new FormField("DetectionMinimumVersion");
            ProjectId = new FormField("ProjectId");
            ProcessesToClose = new FormField("ProcessesToClose");
            Shortcuts = new FormField("Shortcuts");
            Timeout = new FormField("Timeout");
            SuccessCodes = new FormField("SuccessCodes");
            RebootCodes = new FormField("RebootCodes");
            RetryCodes = new FormField("RetryCodes");
            InstallMessage = new FormField("InstallMessage");
            UninstallMessage = new FormField("UninstallMessage");
            DetailMessage = new FormField("DetailMessage");
            Fields = new[]
            {
                SoftwareName, Manufacturer, TargetVersion, ProductCode, InstallArguments, UninstallProgram, UninstallArguments,
                DetectionPath, DetectionMinimumVersion, ProjectId, ProcessesToClose, Shortcuts, Timeout, SuccessCodes,
                RebootCodes, RetryCodes, InstallMessage, UninstallMessage, DetailMessage
            };

            foreach (var field in Fields)
            {
                field.Label = Loc.Get("Form_" + field.Key);
            }

            InstallArguments.Label = InstallArgumentsLabel;
            TargetVersion.Hint = Loc.Get("Form_TargetVersionHint");
            InstallArguments.Hint = IsExe ? Loc.Get("Form_InstallArgumentsHint") : null;
            DetectionPath.Hint = Loc.Get("Form_DetectionPathHint");
            foreach (var field in new[] { ProcessesToClose, Shortcuts, InstallMessage, UninstallMessage, DetailMessage })
            {
                field.IsMultiline = true;
            }

            SoftwareName.Assign(draft.Identity.SoftwareName);
            Manufacturer.Assign(draft.Identity.Manufacturer);
            TargetVersion.Assign(draft.Identity.TargetVersion);
            ProductCode.Assign(draft.Install.ProductCode);
            DetectionMinimumVersion.Assign(draft.Detection.MinimumVersion);
            ProjectId.Assign(_template.ExistingProjectId ?? IntunePackageBuilder.Core.Projects.ProjectId.Suggest(draft.Identity.SoftwareName));
            ProjectId.IsReadOnly = _template.ExistingProjectId != null;
            _projectIdUntouched = _template.ExistingProjectId == null;
            TargetVersion.IsReadOnly = _template.Mode == FormMode.Rebuild;
            InstallArguments.Assign(draft.Install.Arguments);
            UninstallProgram.Assign(draft.Uninstall.ExecutablePath);
            UninstallArguments.Assign(draft.Uninstall.Arguments);
            DetectionPath.Assign(draft.Detection.Path);
            ProcessesToClose.Assign(string.Join(Environment.NewLine, draft.Interaction.ProcessesToClose));
            Shortcuts.Assign(string.Join(Environment.NewLine, draft.PostInstall.SharedShortcutsToRemove.Select(s => s.Root + "|" + s.RelativePath)));
            InstallMessage.Assign(draft.Interaction.InstallMessage);
            UninstallMessage.Assign(draft.Interaction.UninstallMessage);
            DetailMessage.Assign(draft.Interaction.DetailMessage);
            Timeout.Assign(draft.Runtime.TimeoutMinutes.ToString(CultureInfo.InvariantCulture));
            SuccessCodes.Assign(string.Join(", ", draft.Runtime.SuccessCodes));
            RebootCodes.Assign(string.Join(", ", draft.Runtime.RebootCodes));
            RetryCodes.Assign(string.Join(", ", draft.Runtime.RetryCodes));

            TargetVersion.Edited += (sender, args) =>
            {
                if (_minimumFollowsTarget)
                {
                    DetectionMinimumVersion.Assign(TargetVersion.Value);
                }
            };
            DetectionMinimumVersion.Edited += (sender, args) => _minimumFollowsTarget = false;
            SoftwareName.Edited += (sender, args) => ProjectIdFollowsName();
            ProjectId.Edited += (sender, args) => _projectIdUntouched = false;

            Notes = (analysis.Notes ?? new AnalysisNote[0]).Select(Texts.ForNote).ToList();
            Problems = new List<FieldProblem>();
            CreateCommand = new RelayCommand(() => { var ignored = CreateAsync(); }, () => !IsBusy);
            BackCommand = new RelayCommand(() => RaiseBack(), () => !IsBusy);
            ChooseToolCommand = new RelayCommand(() => { var ignored = ChooseToolAsync(); });
            OpenToolDownloadCommand = new RelayCommand(() => _services.Shell.OpenUrl(ToolDownloadUrl));
            ShowAdvancedCommand = new RelayCommand(() => _main.AdvancedMode = true);
            CheckSourceCommand = new RelayCommand(() => { var ignored = CheckSourceAsync(); });

            _main.PropertyChanged += (sender, args) =>
            {
                if (args.PropertyName == "AdvancedMode")
                {
                    RefreshVisibility();
                    Raise("ShowAdoptedHint");
                }
            };
            RefreshVisibility();
        }

        public FormField SoftwareName { get; private set; }

        public FormField Manufacturer { get; private set; }

        public FormField TargetVersion { get; private set; }

        public FormField ProductCode { get; private set; }

        public FormField InstallArguments { get; private set; }

        public FormField UninstallProgram { get; private set; }

        public FormField UninstallArguments { get; private set; }

        public FormField DetectionPath { get; private set; }

        public FormField DetectionMinimumVersion { get; private set; }

        public FormField ProjectId { get; private set; }

        public FormField ProcessesToClose { get; private set; }

        public FormField Shortcuts { get; private set; }

        public FormField Timeout { get; private set; }

        public FormField SuccessCodes { get; private set; }

        public FormField RebootCodes { get; private set; }

        public FormField RetryCodes { get; private set; }

        public FormField InstallMessage { get; private set; }

        public FormField UninstallMessage { get; private set; }

        public FormField DetailMessage { get; private set; }

        public IReadOnlyList<FormField> Fields { get; private set; }

        /// <summary>Hints about the installer that was read (needs its folder, per-user default, vendor input).</summary>
        public IReadOnlyList<string> Notes { get; private set; }

        /// <summary>The heading of the page: it names the base version of an update or a rebuild.</summary>
        public string HeadingText
        {
            get
            {
                switch (_template.Mode)
                {
                    case FormMode.Update:
                        return Loc.Format("Form_UpdateHeading", _template.BasisVersion);
                    case FormMode.Rebuild:
                        return Loc.Format("Form_RebuildHeading", _template.BasisVersion);
                    case FormMode.NewVersion:
                        return Loc.Get("Form_NewVersionHeading");
                    default:
                        return Loc.Get("Form_Heading");
                }
            }
        }

        /// <summary>
        /// Standard mode shows settings adopted from the base version only through this hint, with a direct way to check them
        /// (SPEC 5.3).
        /// </summary>
        public bool ShowAdoptedHint
        {
            get { return _template.AdvancedAdopted && !_main.AdvancedMode; }
        }

        public string AdoptedHintText
        {
            get { return Loc.Format("Form_AdoptedHint", _template.BasisVersion); }
        }

        public ICommand ShowAdvancedCommand { get; private set; }

        public ICommand CheckSourceCommand { get; private set; }

        /// <summary>The metadata read from the installer, one "label: value" line each (advanced view, SPEC 5.3); empty when none were read.</summary>
        public string MetadataText
        {
            get { return DescribeMetadata(); }
        }

        /// <summary>The result of the source check (file count, size, fingerprint); null until the check ran.</summary>
        public string SourceCheckText
        {
            get { return _sourceCheckText; }
            private set { Set(ref _sourceCheckText, value); }
        }

        public MainViewModel Main
        {
            get { return _main; }
        }

        public bool IsMsi
        {
            get { return _analysis.InstallerType == InstallerType.Msi; }
        }

        public bool IsExe
        {
            get { return _analysis.InstallerType == InstallerType.Exe; }
        }

        /// <summary>Text for the label of the install parameters: vendor parameters for an EXE, additional properties for an MSI.</summary>
        public string InstallArgumentsLabel
        {
            get { return Loc.Get(IsMsi ? "Form_InstallArgumentsMsi" : "Form_InstallArgumentsExe"); }
        }

        public string SourceText
        {
            get { return _analysis.Item.Path; }
        }

        public ICommand CreateCommand { get; private set; }

        public ICommand BackCommand { get; private set; }

        public ICommand ChooseToolCommand { get; private set; }

        public ICommand OpenToolDownloadCommand { get; private set; }

        /// <summary>The problems found by the last check, in the order of the form. The first one gets the focus.</summary>
        public IReadOnlyList<FieldProblem> Problems { get; private set; }

        public bool HasProblems
        {
            get { return Problems.Count > 0; }
        }

        public bool IsBusy
        {
            get { return _isBusy; }
            private set
            {
                if (Set(ref _isBusy, value))
                {
                    Raise("IsEditable");
                    ((RelayCommand)CreateCommand).RaiseCanExecuteChanged();
                    ((RelayCommand)BackCommand).RaiseCanExecuteChanged();
                }
            }
        }

        /// <summary>False while a build runs: the entries and the project cannot be changed then (A09).</summary>
        public bool IsEditable
        {
            get { return !_isBusy; }
        }

        public bool NeedsTool
        {
            get { return _needsTool; }
            private set { Set(ref _needsTool, value); }
        }

        public string PhaseText
        {
            get { return _phaseText; }
            private set { Set(ref _phaseText, value); }
        }

        public string ElapsedText
        {
            get { return _elapsedText; }
            private set { Set(ref _elapsedText, value); }
        }

        /// <summary>Why the build failed, as a localized text; null when there is no failure. A failure never leads to a result page.</summary>
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

        public event EventHandler<BuildOutcome> Completed;

        public event EventHandler BackRequested;

        /// <summary>Raised with the key of the first entry that needs attention; the view moves the focus there.</summary>
        public event EventHandler<string> FocusRequested;

        /// <summary>Called by a timer of the view while a build runs.</summary>
        public void UpdateElapsed()
        {
            if (_isBusy)
            {
                ElapsedText = Loc.Format("Build_Elapsed", FormatElapsed(_services.Clock() - _started));
            }
        }

        /// <summary>Builds the configuration from the entries. Entries that cannot be read as numbers or lists are reported in <paramref name="problems"/>.</summary>
        public PackageVersionConfig BuildConfiguration(IList<FieldProblem> problems)
        {
            var config = NewDraft();
            config.Identity.SoftwareName = SoftwareName.Value.Trim();
            config.Identity.Manufacturer = Manufacturer.Value.Trim();
            config.Identity.TargetVersion = TargetVersion.Value.Trim();
            config.Detection.MinimumVersion = DetectionMinimumVersion.Value.Trim();
            config.Install.Arguments = NullIfBlank(InstallArguments.Value);
            if (IsExe)
            {
                config.Uninstall.ExecutablePath = NullIfBlank(UninstallProgram.Value);
                config.Uninstall.Arguments = NullIfBlank(UninstallArguments.Value);
                config.Detection.Path = NullIfBlank(DetectionPath.Value);
            }

            config.Interaction.ProcessesToClose = Lines(ProcessesToClose.Value);
            config.Interaction.InstallMessage = NullIfBlank(InstallMessage.Value);
            config.Interaction.UninstallMessage = NullIfBlank(UninstallMessage.Value);
            config.Interaction.DetailMessage = NullIfBlank(DetailMessage.Value);

            foreach (var line in Lines(Shortcuts.Value))
            {
                var parts = line.Split('|');
                ShortcutRoot root;
                if (parts.Length == 2 && Enum.TryParse(parts[0].Trim(), true, out root) && Enum.IsDefined(typeof(ShortcutRoot), root))
                {
                    config.PostInstall.SharedShortcutsToRemove.Add(new SharedShortcut { Root = root, RelativePath = parts[1].Trim() });
                }
                else
                {
                    problems.Add(new FieldProblem("Shortcuts", Texts.ForValidation(ValidationCode.InvalidShortcut)));
                }
            }

            int timeout;
            if (int.TryParse(Timeout.Value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out timeout))
            {
                config.Runtime.TimeoutMinutes = timeout;
            }
            else
            {
                problems.Add(new FieldProblem("Timeout", Loc.Get("Valid_NotANumber")));
            }

            config.Runtime.SuccessCodes = Codes(SuccessCodes, problems, config.Runtime.SuccessCodes);
            config.Runtime.RebootCodes = Codes(RebootCodes, problems, config.Runtime.RebootCodes);
            config.Runtime.RetryCodes = Codes(RetryCodes, problems, config.Runtime.RetryCodes);
            return config;
        }

        /// <summary>
        /// Checks the entries and shows the problems at the entries (SPEC 5.2). Returns true when everything is valid.
        /// The focus moves to the first entry with a problem.
        /// </summary>
        public bool Validate(out PackageVersionConfig configuration)
        {
            foreach (var field in Fields)
            {
                field.Error = null;
            }

            var found = new List<FieldProblem>();
            configuration = BuildConfiguration(found);
            if (_main.AdvancedMode && !string.IsNullOrWhiteSpace(ProjectId.Value) && Core.Projects.ProjectId.Check(ProjectId.Value.Trim()) != ProjectIdProblem.None)
            {
                found.Add(new FieldProblem("ProjectId", Loc.Get("Valid_ProjectId")));
            }

            if (_template.Mode != FormMode.NewPackage && _template.Mode != FormMode.Rebuild && VersionExists(configuration.Identity.TargetVersion))
            {
                found.Add(new FieldProblem("TargetVersion", Loc.Get("Form_VersionExists")));
            }

            foreach (var issue in ConfigurationValidator.Validate(configuration))
            {
                found.Add(new FieldProblem(KeyFor(issue.Field), Texts.ForValidation(issue.Code)));
            }

            ShowProblems(found);
            return found.Count == 0;
        }

        /// <summary>"Create package": check, then build. Fails only by showing a problem; a failure never opens the result page.</summary>
        public async Task CreateAsync()
        {
            if (IsBusy)
            {
                return;
            }

            ErrorText = null;
            PackageVersionConfig config;
            if (!Validate(out config))
            {
                return;
            }

            var toolPath = _services.Settings.ContentPrepToolPath;
            if (string.IsNullOrWhiteSpace(toolPath) || !File.Exists(toolPath))
            {
                NeedsTool = true;
                return;
            }

            NeedsTool = false;
            var allowUnknown = false;
            if (ContentPrepTool.IdentifyVersion(toolPath) == null)
            {
                var question = Loc.Format("Tool_UnknownConfirm", ContentPrepTool.ComputeSha256(toolPath));
                if (!_services.Dialogs.Confirm(Loc.Get("Tool_UnknownTitle"), question))
                {
                    return;
                }

                allowUnknown = true;
            }

            var folder = PrepareBaseFolder();
            if (folder == null)
            {
                return;
            }

            var request = new NewPackageRequest
            {
                Item = _analysis.Item,
                Configuration = config,
                RequestedProjectId = _template.ExistingProjectId
                    ?? (_main.AdvancedMode && !string.IsNullOrWhiteSpace(ProjectId.Value) ? ProjectId.Value.Trim() : null),
                Language = Loc.Language,
                RuntimeTemplateDirectory = _services.RuntimeTemplateDirectory,
                ContentPrepToolPath = toolPath,
                AllowUnknownTool = allowUnknown,
                WorkRoot = _services.WorkRoot
            };

            _started = _services.Clock();
            PhaseText = null;
            ElapsedText = null;
            IsBusy = true;
            try
            {
                var workflow = new NewPackageWorkflow(folder, _services.Runner, _services.Clock);
                var progress = new Progress<BuildProgress>(p => PhaseText = Texts.ForPhase(p.Phase));
                var outcome = await workflow.RunAsync(request, progress, CancellationToken.None);
                MarkOpened(outcome.ProjectId);
                IsBusy = false;
                var handler = Completed;
                if (handler != null)
                {
                    handler(this, outcome);
                }
            }
            catch (WorkflowException exception)
            {
                IsBusy = false;
                _services.Logger.Log(LogLevel.Warning, "The build did not start", exception);
                if (exception.Problem == WorkflowProblem.ConfigurationInvalid && exception.Issues.Count > 0)
                {
                    ShowProblems(exception.Issues.Select(i => new FieldProblem(KeyFor(i.Field), Texts.ForValidation(i.Code))).ToList());
                }
                else
                {
                    ErrorText = Texts.ForWorkflow(exception);
                }
            }
            catch (BuildFailedException exception)
            {
                IsBusy = false;
                _services.Logger.Log(LogLevel.Error, "The build failed", exception);
                ErrorText = Texts.ForBuild(exception) + " " + LogHint(folder, request, exception);
            }
            catch (Exception exception)
            {
                IsBusy = false;
                _services.Logger.Log(LogLevel.Error, "The build failed unexpectedly", exception);
                ErrorText = Loc.Get("Build_Unexpected");
            }
        }

        private async Task ChooseToolAsync()
        {
            var chosen = _services.Dialogs.PickFile(Loc.Get("Dialog_ChooseTool"), Loc.Get("Dialog_ToolFilter"), null);
            if (chosen == null)
            {
                return;
            }

            _services.Settings.ContentPrepToolPath = chosen;
            _services.SaveSettings();
            NeedsTool = false;
            await CreateAsync();
        }

        private string PrepareBaseFolder()
        {
            var resolution = _services.BaseFolder;
            if (resolution.Status == BaseFolderStatus.Ok)
            {
                return resolution.Path;
            }

            if (resolution.Status == BaseFolderStatus.NotConfigured)
            {
                try
                {
                    Directory.CreateDirectory(resolution.Path);
                    if (BaseFolderResolver.CanWrite(resolution.Path))
                    {
                        _services.Settings.BaseFolder = resolution.Path;
                        _services.SaveSettings();
                        return resolution.Path;
                    }
                }
                catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
                {
                    _services.Logger.Log(LogLevel.Warning, "The proposed base folder cannot be used", exception);
                }
            }

            ErrorText = Loc.Format("Error_BaseFolder", Loc.Format("BaseFolder_" + (resolution.Status == BaseFolderStatus.NotConfigured ? BaseFolderStatus.NotWritable : resolution.Status), resolution.Path));
            return null;
        }

        private void MarkOpened(string projectId)
        {
            try
            {
                _services.Settings.MarkOpened(projectId, _services.Clock());
                _services.SaveSettings();
            }
            catch (Exception exception) when (exception is InvalidProjectIdException)
            {
                _services.Logger.Log(LogLevel.Warning, "The project could not be added to the recent list", exception);
            }
        }

        private string LogHint(string folder, NewPackageRequest request, BuildFailedException exception)
        {
            if (exception.BuildId == null || string.IsNullOrEmpty(request.Configuration.ProjectId))
            {
                return string.Empty;
            }

            try
            {
                var versions = new VersionStore(new ProjectStore(folder));
                var directory = versions.VersionDirectory(request.Configuration.ProjectId, request.Configuration.Identity.TargetVersion.Trim());
                return Loc.Format("Error_LogHint", Path.Combine(directory, BuildPipeline.BuildsFolder, exception.BuildId + ".failed.log"));
            }
            catch (ArgumentException)
            {
                return string.Empty;
            }
        }

        private void ShowProblems(IList<FieldProblem> found)
        {
            var ordered = found
                .OrderBy(p => Array.IndexOf(FieldOrder, p.Key) < 0 ? int.MaxValue : Array.IndexOf(FieldOrder, p.Key))
                .ToList();
            foreach (var problem in ordered)
            {
                var field = Fields.FirstOrDefault(f => f.Key == problem.Key);
                if (field != null && field.Error == null)
                {
                    field.Error = problem.Message;
                }
            }

            Problems = ordered;
            Raise("Problems");
            Raise("HasProblems");
            if (ordered.Count > 0)
            {
                var handler = FocusRequested;
                if (handler != null)
                {
                    handler(this, ordered[0].Key);
                }
            }
        }

        private string DescribeMetadata()
        {
            var lines = new List<string>();
            if (_analysis.Msi != null)
            {
                var msi = _analysis.Msi;
                lines.Add(Line("Meta_ProductName", msi.ProductName));
                lines.Add(Line("Meta_Manufacturer", msi.Manufacturer));
                lines.Add(Line("Meta_ProductVersion", msi.ProductVersion));
                lines.Add(Line("Meta_ProductCode", msi.ProductCode));
                lines.Add(Line("Meta_UpgradeCode", msi.UpgradeCode));
                lines.Add(Line("Meta_AllUsers", msi.AllUsers));
                lines.Add(Loc.Get("Meta_Cabinets") + ": " + (msi.ExternalCabinets.Count == 0 ? Loc.Get("Meta_None") : string.Join(", ", msi.ExternalCabinets)));
                lines.Add(Loc.Get("Meta_NeedsSourceFolder") + ": " + Loc.Get(msi.RequiresSourceFolder ? "Meta_Yes" : "Meta_No"));
            }
            else if (_analysis.Exe != null)
            {
                var exe = _analysis.Exe;
                lines.Add(Line("Meta_ProductName", exe.ProductName));
                lines.Add(Line("Meta_CompanyName", exe.CompanyName));
                lines.Add(Line("Meta_FileDescription", exe.FileDescription));
                lines.Add(Line("Meta_OriginalFilename", exe.OriginalFilename));
                lines.Add(Line("Meta_ProductVersion", exe.ProductVersion));
                lines.Add(Line("Meta_FileVersion", exe.FileVersion));
            }
            else
            {
                return Loc.Get("Meta_NoMetadata");
            }

            return string.Join(Environment.NewLine, lines);
        }

        private static string Line(string labelKey, string value)
        {
            return Loc.Get(labelKey) + ": " + (string.IsNullOrWhiteSpace(value) ? Loc.Get("Meta_NotSet") : value);
        }

        /// <summary>Reads the source the way the build will store it: file count, size and a fingerprint of the content. Nothing is started or changed.</summary>
        public async Task CheckSourceAsync()
        {
            var item = _analysis.Item;
            try
            {
                var result = await Task.Run(() =>
                {
                    if (item.Kind != DroppedKind.Folder)
                    {
                        return Loc.Format("Source_Result", 1, new FileInfo(item.Path).Length, ContentPrepTool.ComputeSha256(item.Path));
                    }

                    var manifest = SourceManifestBuilder.Build(item.Path);
                    return Loc.Format("Source_Result", manifest.Files.Count, manifest.Files.Sum(f => f.Size), manifest.ComputeFingerprint());
                });
                SourceCheckText = result;
            }
            catch (UnsafeSourceException exception)
            {
                _services.Logger.Log(LogLevel.Warning, "The source contains a link", exception);
                SourceCheckText = Loc.Get("Source_Unsafe");
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                _services.Logger.Log(LogLevel.Warning, "The source could not be read", exception);
                SourceCheckText = Loc.Format("Source_Failed", exception.Message);
            }
        }

        /// <summary>A private copy of the starting point: the draft of the template or the plain analysis.</summary>
        private PackageVersionConfig NewDraft()
        {
            return _template.Draft != null ? UpdateDraft.Clone(_template.Draft) : _analysis.CreateConfiguration();
        }

        private bool VersionExists(string version)
        {
            if (_template.ExistingProjectId == null || string.IsNullOrWhiteSpace(version))
            {
                return false;
            }

            var resolution = _services.BaseFolder;
            if (resolution.Status != BaseFolderStatus.Ok)
            {
                return false;
            }

            VersionNumber parsed;
            if (!VersionNumber.TryParse(version.Trim(), out parsed))
            {
                return false;
            }

            try
            {
                return new VersionStore(new ProjectStore(resolution.Path)).List(_template.ExistingProjectId)
                    .Any(e => e.Version != null && e.Version.Equals(parsed));
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                _services.Logger.Log(LogLevel.Warning, "The versions of the project could not be listed", exception);
                return false;
            }
        }

        private string KeyFor(string field)
        {
            switch (field)
            {
                case "identity.softwareName":
                    return "SoftwareName";
                case "identity.manufacturer":
                    return "Manufacturer";
                case "identity.targetVersion":
                    return "TargetVersion";
                case "install.productCode":
                case "uninstall.productCode":
                    return "ProductCode";
                case "install.arguments":
                    return "InstallArguments";
                case "uninstall.executablePath":
                    return "UninstallProgram";
                case "uninstall.arguments":
                    return "UninstallArguments";
                case "detection.path":
                case "detection.method":
                    return "DetectionPath";
                case "detection.minimumVersion":
                    // Without advanced mode the minimum version follows the target version, so that is the entry to fix.
                    return _main.AdvancedMode || !_minimumFollowsTarget ? "DetectionMinimumVersion" : "TargetVersion";
                case "runtime.timeoutMinutes":
                    return "Timeout";
                case "runtime.successCodes":
                case "runtime":
                    return "SuccessCodes";
                case "runtime.rebootCodes":
                    return "RebootCodes";
                case "runtime.retryCodes":
                    return "RetryCodes";
                default:
                    return field.StartsWith("postInstall.", StringComparison.Ordinal) ? "Shortcuts" : "SoftwareName";
            }
        }

        private void RefreshVisibility()
        {
            var advanced = _main.AdvancedMode;
            SoftwareName.IsVisible = true;
            Manufacturer.IsVisible = true;
            TargetVersion.IsVisible = true;
            ProductCode.IsVisible = IsMsi;
            InstallArguments.IsVisible = IsExe || advanced;
            UninstallProgram.IsVisible = IsExe;
            UninstallArguments.IsVisible = IsExe;
            DetectionPath.IsVisible = IsExe;
            foreach (var field in new[] { DetectionMinimumVersion, ProjectId, ProcessesToClose, Shortcuts, Timeout, SuccessCodes, RebootCodes, RetryCodes, InstallMessage, UninstallMessage, DetailMessage })
            {
                field.IsVisible = advanced;
            }

            Raise("IsAdvanced");
        }

        public bool IsAdvanced
        {
            get { return _main.AdvancedMode; }
        }

        private void ProjectIdFollowsName()
        {
            // Only a suggestion for a new project; once the user has typed an ID of their own it stays.
            if (_projectIdUntouched)
            {
                ProjectId.Assign(Core.Projects.ProjectId.Suggest(SoftwareName.Value));
            }
        }

        private bool _projectIdUntouched = true;

        private void RaiseBack()
        {
            var handler = BackRequested;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }

        private static string FormatElapsed(TimeSpan elapsed)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}", (int)elapsed.TotalMinutes, elapsed.Seconds);
        }

        private static string NullIfBlank(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private static List<string> Lines(string value)
        {
            return (value ?? string.Empty)
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim())
                .Where(l => l.Length > 0)
                .ToList();
        }

        private static List<int> Codes(FormField field, IList<FieldProblem> problems, List<int> fallback)
        {
            var codes = new List<int>();
            foreach (var part in (field.Value ?? string.Empty).Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int code;
                if (!int.TryParse(part.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out code))
                {
                    problems.Add(new FieldProblem(field.Key, Loc.Get("Valid_NotACodeList")));
                    return fallback;
                }

                codes.Add(code);
            }

            return codes;
        }
    }
}
