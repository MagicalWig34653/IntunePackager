using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IntunePackageBuilder.Build.Packaging;
using IntunePackageBuilder.Build.Psadt;
using IntunePackageBuilder.Core;
using IntunePackageBuilder.Core.Builds;
using IntunePackageBuilder.Core.Sources;
using IntunePackageBuilder.Core.Storage;
using IntunePackageBuilder.Core.Versions;
using IntunePackageBuilder.Generation;
using IntunePackageBuilder.Generation.Intune;

namespace IntunePackageBuilder.Build.Pipeline
{
    /// <summary>
    /// Builds one package from a stored version (SPEC section 7.2): checks the configuration, locks the version,
    /// verifies the stored source against its manifest, stages the package in a short temporary folder, generates the
    /// text artifacts from one snapshot, packs with the Content Prep Tool, verifies the result and publishes it into
    /// <c>builds/&lt;build-id&gt;</c> in one step. A failed build publishes nothing and removes only what it created.
    /// </summary>
    public sealed class BuildPipeline
    {
        public const string PartialSuffix = ".partial";
        public const string PartialMarkerFileName = ".ipb-partial";
        public const string LogFileName = "build.log";
        public const string BuildsFolder = "builds";
        public const string IntuneFolder = "intune";

        /// <summary>Conservative limit: the classic Windows path limit is 260 characters including the terminating character.</summary>
        public const int MaxPathLength = 259;

        private const long SpaceReserveBytes = 64L * 1024 * 1024;

        /// <summary>Entry script of the toolkit engine; <c>Install.cmd</c> starts it.</summary>
        public const string PsadtEntryScript = "Invoke-AppDeployToolkit.ps1";

        private readonly IContentPrepRunner _runner;
        private readonly Func<DateTime> _utcNow;

        public BuildPipeline(IContentPrepRunner runner)
            : this(runner, () => DateTime.UtcNow)
        {
        }

        public BuildPipeline(IContentPrepRunner runner, Func<DateTime> utcNow)
        {
            if (runner == null)
            {
                throw new ArgumentNullException("runner");
            }

            _runner = runner;
            _utcNow = utcNow ?? (() => DateTime.UtcNow);
        }

        /// <summary>Runs the build on a worker thread, so a user interface stays responsive while it runs.</summary>
        public Task<BuildResult> RunAsync(BuildRequest request, IProgress<BuildProgress> progress, CancellationToken cancellation)
        {
            return Task.Run(() => Run(request, progress, cancellation), cancellation);
        }

        /// <exception cref="BuildFailedException">The build failed; nothing was published.</exception>
        public BuildResult Run(BuildRequest request, IProgress<BuildProgress> progress, CancellationToken cancellation)
        {
            if (request == null)
            {
                throw new ArgumentNullException("request");
            }

            var started = _utcNow();
            var log = new BuildLog(_utcNow);
            var phase = BuildPhase.ValidateConfiguration;
            string buildId = null;
            string buildsDirectory = null;
            try
            {
                Action<BuildPhase> enter = next =>
                {
                    phase = next;
                    cancellation.ThrowIfCancellationRequested();
                    log.Info(next, "start");
                    if (progress != null)
                    {
                        progress.Report(new BuildProgress(next, _utcNow() - started));
                    }
                };

                enter(BuildPhase.ValidateConfiguration);
                var toolkit = ValidateRequest(request);
                var versionDirectory = Path.GetFullPath(request.VersionDirectory);
                buildsDirectory = Path.Combine(versionDirectory, BuildsFolder);

                enter(BuildPhase.AcquireLock);
                using (VersionLock.Acquire(versionDirectory))
                {
                    EnsureWritable(buildsDirectory);
                    RemoveStalePartials(buildsDirectory, log);
                    buildId = NewBuildId(started, buildsDirectory);
                    log.Info(null, "build " + buildId + ", tool " + AppInfo.Version + ", software " + request.Configuration.Identity.TargetVersion);

                    enter(BuildPhase.VerifySource);
                    var sourceDirectory = Path.Combine(versionDirectory, "source");
                    var sourceManifest = VerifySource(request, versionDirectory, sourceDirectory, log);
                    var workRoot = request.WorkRoot ?? Path.Combine(Path.GetTempPath(), "IntunePackageBuilder");
                    CheckSpace(request, sourceManifest, toolkit, workRoot, buildsDirectory);

                    enter(BuildPhase.PrepareWorkspace);
                    Directory.CreateDirectory(workRoot);
                    using (var workspace = BuildWorkspace.Create(workRoot, buildId))
                    {
                        log.Info(null, "working folder " + workspace.Root);

                        enter(BuildPhase.StagePackage);
                        Stage(request, sourceManifest, sourceDirectory, toolkit, workspace);

                        enter(BuildPhase.GenerateArtifacts);
                        var snapshot = BuildSnapshot.Create(request.Configuration, sourceManifest, buildId, request.Language, started);
                        BuildArtifacts.Write(snapshot, request.IntuneOptions, workspace.IntuneDirectory, workspace.PackageDirectory);

                        enter(BuildPhase.PackContent);
                        var packed = Pack(request, workspace, log);

                        enter(BuildPhase.VerifyPackage);
                        var package = Path.Combine(workspace.OutputDirectory, DeploymentInterface.PackageFileName(request.Configuration.ProjectId));
                        if (!string.Equals(packed, package, StringComparison.OrdinalIgnoreCase))
                        {
                            File.Move(packed, package);
                        }

                        Wrap(BuildPhase.VerifyPackage, () => IntunewinVerifier.Verify(package));

                        enter(BuildPhase.Publish);
                        var result = Publish(request, snapshot, sourceManifest, toolkit, workspace, package, buildsDirectory, buildId, started, log);

                        enter(BuildPhase.Cleanup);
                        log.Info(BuildPhase.Cleanup, "done");
                        TryWriteLog(Path.Combine(result.BuildDirectory, LogFileName), log);
                        result.Files = Directory.EnumerateFiles(result.BuildDirectory, "*", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal).ToList();
                        return result;
                    }
                }
            }
            catch (BuildFailedException exception)
            {
                exception.BuildId = buildId;
                log.Error(exception.Phase, exception.Message);
                WriteFailureLog(buildsDirectory, buildId, log);
                throw;
            }
            catch (OperationCanceledException)
            {
                log.Error(phase, "cancelled");
                WriteFailureLog(buildsDirectory, buildId, log);
                throw new BuildFailedException(phase, BuildProblem.Cancelled, "cancelled") { BuildId = buildId };
            }
            catch (Exception exception)
            {
                var failure = Translate(phase, exception);
                failure.BuildId = buildId;
                log.Error(phase, failure.Message + " | " + exception);
                WriteFailureLog(buildsDirectory, buildId, log);
                throw failure;
            }
        }

        /// <summary>Checks the request and returns the checked toolkit ZIP when the configuration selects the PSAppDeployToolkit, otherwise null.</summary>
        private static PsadtPackageInfo ValidateRequest(BuildRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.VersionDirectory) || !Directory.Exists(request.VersionDirectory))
            {
                throw new BuildFailedException(BuildPhase.ValidateConfiguration, BuildProblem.RequestInvalid, "version folder: " + request.VersionDirectory);
            }

            if (request.Configuration == null)
            {
                throw new BuildFailedException(BuildPhase.ValidateConfiguration, BuildProblem.RequestInvalid, "no configuration");
            }

            if (Array.IndexOf(BuildSnapshot.SupportedLanguages, request.Language) < 0)
            {
                throw new BuildFailedException(BuildPhase.ValidateConfiguration, BuildProblem.RequestInvalid, "language: " + request.Language);
            }

            var issues = ConfigurationValidator.Validate(request.Configuration);
            if (issues.Count > 0)
            {
                throw new BuildFailedException(
                    BuildPhase.ValidateConfiguration,
                    BuildProblem.ConfigurationInvalid,
                    string.Join(", ", issues.Select(i => i.ToString())));
            }

            if (string.IsNullOrWhiteSpace(request.RuntimeTemplateDirectory)
                || !File.Exists(Path.Combine(request.RuntimeTemplateDirectory, DeploymentInterface.EntryScript)))
            {
                throw new BuildFailedException(
                    BuildPhase.ValidateConfiguration,
                    BuildProblem.TemplateMissing,
                    DeploymentInterface.EntryScript + " in " + request.RuntimeTemplateDirectory);
            }

            return request.Configuration.Deployment.Engine == DeploymentEngine.Psadt ? ValidateToolkit(request) : null;
        }

        private static PsadtPackageInfo ValidateToolkit(BuildRequest request)
        {
            var supply = request.Psadt;
            if (supply == null || string.IsNullOrWhiteSpace(supply.PackagePath) || !File.Exists(supply.PackagePath))
            {
                throw new BuildFailedException(BuildPhase.ValidateConfiguration, BuildProblem.ToolkitMissing, supply == null ? "no toolkit supplied" : supply.PackagePath);
            }

            foreach (var name in new[] { DeploymentInterface.EntryScript, PsadtEntryScript })
            {
                if (string.IsNullOrWhiteSpace(supply.TemplateDirectory) || !File.Exists(Path.Combine(supply.TemplateDirectory, name)))
                {
                    throw new BuildFailedException(BuildPhase.ValidateConfiguration, BuildProblem.TemplateMissing, name + " in " + supply.TemplateDirectory);
                }
            }

            PsadtPackageInfo info;
            try
            {
                info = PsadtPackage.Inspect(supply.PackagePath, supply.PinPath, supply.AllowUnknown);
            }
            catch (PsadtException exception)
            {
                var problem = exception.Problem == PsadtProblem.PackageNotRecognized
                    ? BuildProblem.ToolkitNotRecognized
                    : exception.Problem == PsadtProblem.PackageMissing ? BuildProblem.ToolkitMissing : BuildProblem.ToolkitInvalid;
                throw new BuildFailedException(BuildPhase.ValidateConfiguration, problem, exception.Detail, exception);
            }

            var missing = PsadtAssets.Missing(Path.GetFullPath(request.VersionDirectory), request.Configuration.Deployment.Psadt);
            if (missing.Count > 0)
            {
                throw new BuildFailedException(BuildPhase.ValidateConfiguration, BuildProblem.ToolkitAssetsMissing, string.Join(", ", missing));
            }

            return info;
        }

        private static void EnsureWritable(string buildsDirectory)
        {
            try
            {
                Directory.CreateDirectory(buildsDirectory);
                var probe = Path.Combine(buildsDirectory, ".write-test-" + Guid.NewGuid().ToString("N"));
                File.WriteAllText(probe, "x");
                File.Delete(probe);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                throw new BuildFailedException(BuildPhase.AcquireLock, BuildProblem.NotWritable, buildsDirectory + " (" + exception.Message + ")", exception);
            }
        }

        /// <summary>Removes unfinished publish folders of earlier, interrupted builds (only those with the marker; the version lock is held).</summary>
        private static void RemoveStalePartials(string buildsDirectory, BuildLog log)
        {
            foreach (var directory in Directory.GetDirectories(buildsDirectory, "*" + PartialSuffix))
            {
                if (MarkedFolder.RemoveIfOwned(directory, PartialMarkerFileName, null))
                {
                    log.Info(null, "removed unfinished build folder " + Path.GetFileName(directory));
                }
            }
        }

        private static string NewBuildId(DateTime started, string buildsDirectory)
        {
            for (var attempt = 0; attempt < 20; attempt++)
            {
                var id = BuildId.New(started);
                if (!Directory.Exists(Path.Combine(buildsDirectory, id)) && !Directory.Exists(Path.Combine(buildsDirectory, id + PartialSuffix)))
                {
                    return id;
                }
            }

            throw new BuildFailedException(BuildPhase.AcquireLock, BuildProblem.Unexpected, "no free build ID");
        }

        private static SourceManifest VerifySource(BuildRequest request, string versionDirectory, string sourceDirectory, BuildLog log)
        {
            var manifestPath = Path.Combine(versionDirectory, SourceManifest.FileName);
            if (!File.Exists(manifestPath) || !Directory.Exists(sourceDirectory))
            {
                throw new BuildFailedException(BuildPhase.VerifySource, BuildProblem.SourceMissing, "no stored source or manifest in " + versionDirectory);
            }

            var manifest = SourceManifestStore.Read(manifestPath);
            try
            {
                SourceManifestBuilder.RequireUnchanged(sourceDirectory, manifest);
            }
            catch (SourceChangedException exception)
            {
                throw new BuildFailedException(
                    BuildPhase.VerifySource,
                    BuildProblem.SourceChanged,
                    string.Join(", ", exception.Differences.Select(d => d.ToString())),
                    exception);
            }

            var installer = request.Configuration.Source.InstallerRelativePath.Trim().Replace('\\', '/');
            if (!manifest.Files.Any(f => string.Equals(f.Path, installer, StringComparison.OrdinalIgnoreCase)))
            {
                throw new BuildFailedException(BuildPhase.VerifySource, BuildProblem.SourceMissing, "the installer is not part of the stored source: " + installer);
            }

            log.Info(BuildPhase.VerifySource, manifest.Files.Count + " file(s) verified, fingerprint " + manifest.ComputeFingerprint());
            return manifest;
        }

        private static void CheckSpace(BuildRequest request, SourceManifest manifest, PsadtPackageInfo toolkit, string workRoot, string buildsDirectory)
        {
            var probe = request.FreeSpaceProbe ?? FreeBytes;
            var needed = ((manifest.Files.Sum(f => f.Size) + (toolkit == null ? 0 : toolkit.UncompressedBytes)) * 3) + SpaceReserveBytes;
            foreach (var location in new[] { workRoot, buildsDirectory })
            {
                var free = probe(location);
                if (free < needed)
                {
                    throw new BuildFailedException(
                        BuildPhase.VerifySource,
                        BuildProblem.NotEnoughSpace,
                        location + ": " + free + " bytes free, about " + needed + " needed");
                }
            }
        }

        private static long FreeBytes(string path)
        {
            try
            {
                var root = Path.GetPathRoot(Path.GetFullPath(path));
                if (string.IsNullOrEmpty(root) || root.StartsWith("\\\\", StringComparison.Ordinal))
                {
                    return long.MaxValue;
                }

                return new DriveInfo(root).AvailableFreeSpace;
            }
            catch (ArgumentException)
            {
                return long.MaxValue;
            }
            catch (IOException)
            {
                return long.MaxValue;
            }
        }

        private static void Stage(BuildRequest request, SourceManifest manifest, string sourceDirectory, PsadtPackageInfo toolkit, BuildWorkspace workspace)
        {
            if (toolkit == null)
            {
                CopyTemplates(request.RuntimeTemplateDirectory, workspace.PackageDirectory, null);
            }
            else
            {
                // The runtime functions are shared; the entry points of the two engines differ (the toolkit's replaces the wrapper's).
                CopyTemplates(request.RuntimeTemplateDirectory, workspace.PackageDirectory, name => name != DeploymentInterface.EntryScript && name != "Deploy-Wrapper.ps1");
                CopyTemplates(request.Psadt.TemplateDirectory, workspace.PackageDirectory, null);
                PsadtPackage.ExtractModule(request.Psadt.PackagePath, workspace.PackageDirectory, RequirePathLength);
                StageAssets(request, workspace.PackageDirectory);
            }

            var filesRoot = Path.Combine(workspace.PackageDirectory, DeploymentInterface.PackageSourceFolder);
            foreach (var file in manifest.Files)
            {
                var relative = file.Path.Replace('/', Path.DirectorySeparatorChar);
                var target = Path.Combine(filesRoot, relative);
                RequirePathLength(target);
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Copy(Path.Combine(sourceDirectory, relative), target, false);
            }
        }

        private static void StageAssets(BuildRequest request, string packageRoot)
        {
            var source = PsadtAssets.AssetsDirectory(Path.GetFullPath(request.VersionDirectory));
            foreach (var name in PsadtAssets.Referenced(request.Configuration.Deployment.Psadt))
            {
                var target = Path.Combine(packageRoot, Generation.Psadt.PsadtOverlay.AssetsFolder, name);
                RequirePathLength(target);
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Copy(Path.Combine(source, name), target, false);
            }
        }

        private static void CopyTemplates(string templateDirectory, string packageRoot, Func<string, bool> include)
        {
            var root = Path.GetFullPath(templateDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new BuildFailedException(BuildPhase.StagePackage, BuildProblem.TemplateMissing, "a template file is a link: " + file);
                }

                var relative = file.Substring(root.Length);
                if (include != null && !include(relative))
                {
                    continue;
                }

                var target = Path.Combine(packageRoot, relative);
                RequirePathLength(target);
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Copy(file, target, false);
            }
        }

        private static void RequirePathLength(string path)
        {
            if (path.Length > MaxPathLength)
            {
                throw new BuildFailedException(BuildPhase.StagePackage, BuildProblem.PathTooLong, path.Length + " characters: " + path);
            }
        }

        private string Pack(BuildRequest request, BuildWorkspace workspace, BuildLog log)
        {
            string toolVersion = null;
            if (!string.IsNullOrWhiteSpace(request.ContentPrepToolPath) && File.Exists(request.ContentPrepToolPath))
            {
                toolVersion = ContentPrepTool.IdentifyVersion(request.ContentPrepToolPath);
                log.Info(BuildPhase.PackContent, "Content Prep Tool " + (toolVersion ?? "unknown version") + ", SHA-256 " + ContentPrepTool.ComputeSha256(request.ContentPrepToolPath));
            }

            return _runner.Pack(new PackRequest
            {
                ToolPath = request.ContentPrepToolPath,
                SetupFolder = workspace.PackageDirectory,
                SetupFile = DeploymentInterface.EntryScript,
                OutputFolder = workspace.OutputDirectory,
                Timeout = request.ToolTimeout,
                AllowUnknownTool = request.AllowUnknownTool
            });
        }

        private BuildResult Publish(
            BuildRequest request,
            BuildSnapshot snapshot,
            SourceManifest sourceManifest,
            PsadtPackageInfo toolkit,
            BuildWorkspace workspace,
            string package,
            string buildsDirectory,
            string buildId,
            DateTime started,
            BuildLog log)
        {
            var partial = Path.Combine(buildsDirectory, buildId + PartialSuffix);
            var final = Path.Combine(buildsDirectory, buildId);
            try
            {
                Directory.CreateDirectory(partial);
                File.WriteAllText(Path.Combine(partial, PartialMarkerFileName), buildId, new UTF8Encoding(false));

                var packageName = Path.GetFileName(package);
                var publishedPackage = Path.Combine(partial, packageName);
                CopyVerified(package, publishedPackage);

                var intune = Path.Combine(partial, IntuneFolder);
                Directory.CreateDirectory(intune);
                foreach (var file in Directory.GetFiles(workspace.IntuneDirectory))
                {
                    CopyVerified(file, Path.Combine(intune, Path.GetFileName(file)));
                }

                BuildSnapshotStore.WriteNew(Path.Combine(partial, BuildSnapshot.FileName), snapshot);

                var manifest = new BuildManifest
                {
                    BuildId = buildId,
                    CreatedUtc = started,
                    ProjectId = request.Configuration.ProjectId,
                    SoftwareVersion = request.Configuration.Identity.TargetVersion.Trim(),
                    Language = request.Language,
                    ToolVersion = AppInfo.Version,
                    SourceFingerprint = sourceManifest.ComputeFingerprint(),
                    Package = Describe(partial, publishedPackage)
                };
                if (!string.IsNullOrWhiteSpace(request.ContentPrepToolPath) && File.Exists(request.ContentPrepToolPath))
                {
                    manifest.ContentPrepToolVersion = ContentPrepTool.IdentifyVersion(request.ContentPrepToolPath);
                    manifest.ContentPrepToolSha256 = ContentPrepTool.ComputeSha256(request.ContentPrepToolPath);
                }

                if (toolkit != null)
                {
                    manifest.ToolkitVersion = toolkit.Version;
                    manifest.ToolkitSha256 = toolkit.Sha256;
                    manifest.ToolkitAssets = new List<BuildFileEntry>();
                    var assetsRoot = Path.GetDirectoryName(PsadtAssets.AssetsDirectory(Path.GetFullPath(request.VersionDirectory)));
                    foreach (var name in PsadtAssets.Referenced(request.Configuration.Deployment.Psadt).OrderBy(n => n, StringComparer.Ordinal))
                    {
                        manifest.ToolkitAssets.Add(Describe(assetsRoot, Path.Combine(PsadtAssets.AssetsDirectory(Path.GetFullPath(request.VersionDirectory)), name)));
                    }
                }

                foreach (var file in Directory.EnumerateFiles(partial, "*", SearchOption.AllDirectories)
                    .Where(f => !string.Equals(Path.GetFileName(f), PartialMarkerFileName, StringComparison.Ordinal) && f != publishedPackage)
                    .OrderBy(f => f, StringComparer.Ordinal))
                {
                    manifest.Files.Add(Describe(partial, file));
                }

                BuildManifestStore.WriteNew(Path.Combine(partial, BuildManifest.FileName), manifest);
                File.Delete(Path.Combine(partial, PartialMarkerFileName));
                Directory.Move(partial, final);
            }
            catch (Exception exception) when (!(exception is BuildFailedException) && !(exception is OperationCanceledException))
            {
                MarkedFolder.RemoveIfOwned(partial, PartialMarkerFileName, buildId);
                throw new BuildFailedException(BuildPhase.Publish, BuildProblem.PublishFailed, exception.Message, exception);
            }

            log.Info(BuildPhase.Publish, "published " + final);
            return new BuildResult
            {
                BuildId = buildId,
                BuildDirectory = final,
                PackagePath = Path.Combine(final, Path.GetFileName(package)),
                IntuneDirectory = Path.Combine(final, IntuneFolder),
                Files = Directory.EnumerateFiles(final, "*", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal).ToList()
            };
        }

        private static void CopyVerified(string source, string target)
        {
            RequirePublishPathLength(target);
            File.Copy(source, target, false);
            if (!string.Equals(ContentPrepTool.ComputeSha256(source), ContentPrepTool.ComputeSha256(target), StringComparison.Ordinal))
            {
                throw new IOException("The copy differs from the original: " + target);
            }
        }

        private static void RequirePublishPathLength(string path)
        {
            if (path.Length > MaxPathLength)
            {
                throw new IOException("The path is too long (" + path.Length + " characters): " + path);
            }
        }

        private static BuildFileEntry Describe(string root, string file)
        {
            return new BuildFileEntry
            {
                Path = file.Substring(root.TrimEnd(Path.DirectorySeparatorChar).Length + 1).Replace('\\', '/'),
                Size = new FileInfo(file).Length,
                Sha256 = ContentPrepTool.ComputeSha256(file)
            };
        }

        private static void Wrap(BuildPhase phase, Action action)
        {
            try
            {
                action();
            }
            catch (ContentPrepException exception)
            {
                throw new BuildFailedException(phase, BuildProblem.PackagingFailed, exception.Message, exception);
            }
        }

        private static BuildFailedException Translate(BuildPhase phase, Exception exception)
        {
            if (exception is LockHeldException)
            {
                return new BuildFailedException(phase, BuildProblem.VersionLocked, ((LockHeldException)exception).LockPath, exception);
            }

            if (exception is ContentPrepException)
            {
                return new BuildFailedException(phase, BuildProblem.PackagingFailed, exception.Message, exception);
            }

            if (exception is StorageFormatException || exception is UnsupportedSchemaException)
            {
                return new BuildFailedException(phase, BuildProblem.SourceMissing, exception.Message, exception);
            }

            var win32Error = exception.HResult & 0xFFFF;
            if (exception is IOException && (win32Error == 112 || win32Error == 39))
            {
                // ERROR_DISK_FULL and ERROR_HANDLE_DISK_FULL.
                return new BuildFailedException(phase, BuildProblem.NotEnoughSpace, exception.Message, exception);
            }

            if (exception is IOException || exception is UnauthorizedAccessException)
            {
                return new BuildFailedException(phase, BuildProblem.NotWritable, exception.Message, exception);
            }

            return new BuildFailedException(phase, BuildProblem.Unexpected, exception.GetType().Name + ": " + exception.Message, exception);
        }

        private static void WriteFailureLog(string buildsDirectory, string buildId, BuildLog log)
        {
            if (buildsDirectory == null || buildId == null)
            {
                return;
            }

            TryWriteLog(Path.Combine(buildsDirectory, buildId + ".failed.log"), log);
        }

        private static void TryWriteLog(string path, BuildLog log)
        {
            try
            {
                if (Directory.Exists(Path.GetDirectoryName(path)))
                {
                    File.WriteAllText(path, log.ToString(), new UTF8Encoding(false));
                }
            }
            catch (IOException)
            {
                // The log is best effort; the exception of the build is what matters.
            }
            catch (UnauthorizedAccessException)
            {
                // See above.
            }
        }
    }
}
