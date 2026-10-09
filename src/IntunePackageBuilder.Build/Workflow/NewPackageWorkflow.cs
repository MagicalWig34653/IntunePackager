using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IntunePackageBuilder.Build.Packaging;
using IntunePackageBuilder.Build.Pipeline;
using IntunePackageBuilder.Core.Builds;
using IntunePackageBuilder.Core.Projects;
using IntunePackageBuilder.Core.Sources;
using IntunePackageBuilder.Core.Storage;
using IntunePackageBuilder.Core.Versions;
using IntunePackageBuilder.Generation.Intune;

namespace IntunePackageBuilder.Build.Workflow
{
    public enum WorkflowProblem
    {
        /// <summary>The configuration is incomplete or invalid; the issues are in <see cref="WorkflowException.Issues"/>. Nothing was created.</summary>
        ConfigurationInvalid,

        /// <summary>The project ID is not valid.</summary>
        InvalidProjectId,

        /// <summary>No Content Prep Tool is configured (or the file is gone).</summary>
        ToolNotConfigured,

        /// <summary>The source was refused; <see cref="WorkflowException.ImportProblem"/> says why. A project created by this call is removed again.</summary>
        SourceRejected,

        /// <summary>A stored version with a different installer already exists; a changed installer needs a new version (SPEC 6.4).</summary>
        SourceAlreadyStored,

        /// <summary>Another process works on this project version.</summary>
        VersionLocked,

        /// <summary>Reading or writing the project data failed.</summary>
        StorageFailed
    }

    /// <summary>The workflow stopped before the build started. Carries codes, never display text.</summary>
    public sealed class WorkflowException : Exception
    {
        public WorkflowException(WorkflowProblem problem, string detail, Exception inner = null)
            : base(problem + (string.IsNullOrEmpty(detail) ? string.Empty : ": " + detail), inner)
        {
            Problem = problem;
            Detail = detail ?? string.Empty;
            Issues = new List<ValidationIssue>();
        }

        public WorkflowProblem Problem { get; private set; }

        public string Detail { get; private set; }

        public IList<ValidationIssue> Issues { get; private set; }

        public ImportProblem? ImportProblem { get; set; }
    }

    public sealed class NewPackageRequest
    {
        public NewPackageRequest()
        {
            Language = "en";
        }

        /// <summary>What the user dropped or picked.</summary>
        public DroppedItem Item { get; set; }

        /// <summary>The form content. <see cref="PackageVersionConfig.ProjectId"/> is set by the workflow.</summary>
        public PackageVersionConfig Configuration { get; set; }

        /// <summary>Project ID for a new project (advanced mode); null derives it from the software name.</summary>
        public string RequestedProjectId { get; set; }

        /// <summary>Display name for a new project; null uses the software name.</summary>
        public string DisplayName { get; set; }

        public string Language { get; set; }

        public string RuntimeTemplateDirectory { get; set; }

        public string ContentPrepToolPath { get; set; }

        public bool AllowUnknownTool { get; set; }

        public string WorkRoot { get; set; }

        public IntuneSettingsOptions IntuneOptions { get; set; }
    }

    /// <summary>The result page of a successful build. Always describes the build that just finished, never an earlier one.</summary>
    public sealed class BuildOutcome
    {
        public string ProjectId { get; set; }

        public bool ProjectWasCreated { get; set; }

        public string SoftwareName { get; set; }

        public string TargetVersion { get; set; }

        public string BuildId { get; set; }

        public string BuildDirectory { get; set; }

        public string PackagePath { get; set; }

        public string IntuneDirectory { get; set; }

        public string GuidePath { get; set; }

        public string LogPath { get; set; }

        public string InstallCommand { get; set; }

        public string UninstallCommand { get; set; }

        public string InstallContext { get; set; }

        /// <summary>Detection script name and rule description (file or product code).</summary>
        public string DetectionScript { get; set; }

        public string DetectionTarget { get; set; }

        /// <summary>The Intune values of this build as <c>key: value</c> lines, for the copy function. Never a global template.</summary>
        public string ClipboardText { get; set; }
    }

    /// <summary>
    /// The standard-mode workflow behind "Create package" (SPEC 5.2): checks the form, creates the project and the
    /// version only now, stores the source, builds and describes the result. A project that this call created is
    /// removed again when the source is refused; a failed build keeps project and version so the user can fix the
    /// cause and build again.
    /// </summary>
    public sealed class NewPackageWorkflow
    {
        private readonly string _baseFolder;
        private readonly IContentPrepRunner _runner;
        private readonly Func<DateTime> _utcNow;

        public NewPackageWorkflow(string baseFolder, IContentPrepRunner runner)
            : this(baseFolder, runner, () => DateTime.UtcNow)
        {
        }

        public NewPackageWorkflow(string baseFolder, IContentPrepRunner runner, Func<DateTime> utcNow)
        {
            if (string.IsNullOrWhiteSpace(baseFolder))
            {
                throw new ArgumentException("A base folder is required.", "baseFolder");
            }

            if (runner == null)
            {
                throw new ArgumentNullException("runner");
            }

            _baseFolder = baseFolder;
            _runner = runner;
            _utcNow = utcNow ?? (() => DateTime.UtcNow);
        }

        public Task<BuildOutcome> RunAsync(NewPackageRequest request, IProgress<BuildProgress> progress, CancellationToken cancellation)
        {
            return Task.Run(() => Run(request, progress, cancellation), cancellation);
        }

        /// <exception cref="WorkflowException">Stopped before the build.</exception>
        /// <exception cref="BuildFailedException">The build failed; nothing was published.</exception>
        public BuildOutcome Run(NewPackageRequest request, IProgress<BuildProgress> progress, CancellationToken cancellation)
        {
            if (request == null)
            {
                throw new ArgumentNullException("request");
            }

            var config = request.Configuration;
            if (config == null || request.Item == null)
            {
                throw new WorkflowException(WorkflowProblem.ConfigurationInvalid, "no configuration or source");
            }

            var issues = ConfigurationValidator.Validate(config);
            if (issues.Count > 0)
            {
                var invalid = new WorkflowException(WorkflowProblem.ConfigurationInvalid, string.Join(", ", issues.Select(i => i.ToString())));
                foreach (var issue in issues)
                {
                    invalid.Issues.Add(issue);
                }

                throw invalid;
            }

            if (string.IsNullOrWhiteSpace(request.ContentPrepToolPath) || !File.Exists(request.ContentPrepToolPath))
            {
                throw new WorkflowException(WorkflowProblem.ToolNotConfigured, request.ContentPrepToolPath);
            }

            var projectId = string.IsNullOrWhiteSpace(request.RequestedProjectId) ? ProjectId.Suggest(config.Identity.SoftwareName) : request.RequestedProjectId.Trim();
            if (ProjectId.Check(projectId) != ProjectIdProblem.None)
            {
                throw new WorkflowException(WorkflowProblem.InvalidProjectId, projectId);
            }

            config.ProjectId = projectId;
            var projects = new ProjectStore(_baseFolder);
            var versions = new VersionStore(projects);
            var version = config.Identity.TargetVersion.Trim();
            var createdProject = false;
            string versionDirectory;
            PackageVersionConfig stored;
            try
            {
                createdProject = EnsureProject(projects, projectId, string.IsNullOrWhiteSpace(request.DisplayName) ? config.Identity.SoftwareName : request.DisplayName);
                try
                {
                    versions.Load(projectId, version);
                    versions.Save(projectId, version, config);
                }
                catch (VersionNotFoundException)
                {
                    versions.Create(projectId, config);
                }

                versionDirectory = versions.VersionDirectory(projectId, version);
                if (!File.Exists(Path.Combine(versionDirectory, SourceManifest.FileName)))
                {
                    var imported = versions.ImportSource(projectId, version, request.Item, config.Source.InstallerRelativePath);
                    config.Source.InstallerRelativePath = imported.InstallerRelativePath;
                    versions.Save(projectId, version, config);
                }

                else
                {
                    RequireSameSource(request.Item, Path.Combine(versionDirectory, SourceManifest.FileName));
                }

                stored = versions.Load(projectId, version);
            }
            catch (ImportRejectedException exception)
            {
                RollbackProject(projects, projectId, createdProject);
                var rejected = new WorkflowException(
                    exception.Problem == ImportProblem.SourceAlreadyStored ? WorkflowProblem.SourceAlreadyStored : WorkflowProblem.SourceRejected,
                    exception.Path,
                    exception);
                rejected.ImportProblem = exception.Problem;
                throw rejected;
            }
            catch (LockHeldException exception)
            {
                RollbackProject(projects, projectId, createdProject);
                throw new WorkflowException(WorkflowProblem.VersionLocked, exception.LockPath, exception);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is StorageFormatException || exception is UnsupportedSchemaException || exception is ProjectExistsException || exception is VersionExistsException)
            {
                RollbackProject(projects, projectId, createdProject);
                throw new WorkflowException(WorkflowProblem.StorageFailed, exception.Message, exception);
            }

            var pipeline = new BuildPipeline(_runner, _utcNow);
            var result = pipeline.Run(
                new BuildRequest
                {
                    VersionDirectory = versionDirectory,
                    Configuration = stored,
                    Language = request.Language,
                    RuntimeTemplateDirectory = request.RuntimeTemplateDirectory,
                    ContentPrepToolPath = request.ContentPrepToolPath,
                    AllowUnknownTool = request.AllowUnknownTool,
                    WorkRoot = request.WorkRoot,
                    IntuneOptions = request.IntuneOptions
                },
                progress,
                cancellation);

            return Describe(result, projectId, createdProject, request.IntuneOptions);
        }

        /// <summary>Describes a published build for the result page, from the files of that build.</summary>
        public static BuildOutcome Describe(BuildResult result, string projectId, bool projectWasCreated, IntuneSettingsOptions options)
        {
            var snapshot = BuildSnapshotStore.Read(Path.Combine(result.BuildDirectory, BuildSnapshot.FileName));
            var settings = IntuneSettingsBuilder.From(snapshot, options ?? new IntuneSettingsOptions());
            var rule = settings.Detection.Rule;
            var lines = SettingsWriter.Flatten(settings).Select(pair => pair.Key + ": " + pair.Value);
            return new BuildOutcome
            {
                ProjectId = projectId,
                ProjectWasCreated = projectWasCreated,
                SoftwareName = settings.App.Name,
                TargetVersion = settings.App.Version,
                BuildId = result.BuildId,
                BuildDirectory = result.BuildDirectory,
                PackagePath = result.PackagePath,
                IntuneDirectory = result.IntuneDirectory,
                GuidePath = Path.Combine(result.IntuneDirectory, DeploymentInterface.GuideFile),
                LogPath = Path.Combine(result.BuildDirectory, BuildPipeline.LogFileName),
                InstallCommand = settings.Program.InstallCommand,
                UninstallCommand = settings.Program.UninstallCommand,
                InstallContext = settings.Program.InstallContext,
                DetectionScript = settings.Detection.ScriptFileName,
                DetectionTarget = rule.Method == DetectionMethod.MsiProductCode ? rule.ProductCode : rule.Path,
                ClipboardText = string.Join(Environment.NewLine, lines)
            };
        }

        /// <summary>
        /// A stored source is never replaced. When the version already has one, the dropped item must be the same
        /// content, otherwise the user would silently get a build of the old installer (SPEC 6.4).
        /// </summary>
        private static void RequireSameSource(DroppedItem item, string manifestPath)
        {
            var stored = SourceManifestStore.Read(manifestPath);
            bool same;
            try
            {
                if (item.Kind != DroppedKind.Folder)
                {
                    same = stored.Files.Count == 1 && string.Equals(stored.Files[0].Sha256, ContentPrepTool.ComputeSha256(item.Path), StringComparison.Ordinal);
                }
                else
                {
                    same = string.Equals(SourceManifestBuilder.Build(item.Path).ComputeFingerprint(), stored.ComputeFingerprint(), StringComparison.Ordinal);
                }
            }
            catch (UnsafeSourceException)
            {
                same = false;
            }

            if (!same)
            {
                var different = new WorkflowException(WorkflowProblem.SourceAlreadyStored, item.Path);
                different.ImportProblem = ImportProblem.SourceAlreadyStored;
                throw different;
            }
        }

        private static bool EnsureProject(ProjectStore projects, string projectId, string displayName)
        {
            try
            {
                projects.Load(projectId);
                return false;
            }
            catch (ProjectNotFoundException)
            {
                projects.Create(projectId, displayName);
                return true;
            }
        }

        /// <summary>Removes a project this call created, so a refused source leaves no empty project behind.</summary>
        private static void RollbackProject(ProjectStore projects, string projectId, bool createdByThisCall)
        {
            if (!createdByThisCall)
            {
                return;
            }

            try
            {
                var directory = projects.ProjectDirectory(projectId);
                var baseFolder = Path.GetFullPath(projects.BaseFolder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (Directory.Exists(directory) && Path.GetFullPath(directory).StartsWith(baseFolder, StringComparison.OrdinalIgnoreCase))
                {
                    Directory.Delete(directory, true);
                }
            }
            catch (IOException)
            {
                // A leftover empty project folder is harmless; the original error is what matters.
            }
            catch (UnauthorizedAccessException)
            {
                // See above.
            }
        }
    }
}
