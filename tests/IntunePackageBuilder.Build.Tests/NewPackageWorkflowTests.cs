using System;
using System.IO;
using System.Linq;
using System.Threading;
using IntunePackageBuilder.Build.Packaging;
using IntunePackageBuilder.Build.Pipeline;
using IntunePackageBuilder.Build.Workflow;
using IntunePackageBuilder.Core.Projects;
using IntunePackageBuilder.Core.Sources;
using IntunePackageBuilder.Core.Versions;
using Xunit;

namespace IntunePackageBuilder.Build.Tests
{
    public class NewPackageWorkflowTests : IDisposable
    {
        private readonly TempDirectory _directory = new TempDirectory();

        public NewPackageWorkflowTests()
        {
            BaseFolder = _directory.Combine("projects");
            Directory.CreateDirectory(BaseFolder);
            TemplateDirectory = _directory.Combine("template");
            Directory.CreateDirectory(TemplateDirectory);
            File.WriteAllText(Path.Combine(TemplateDirectory, "Install.cmd"), "@echo off\r\n");
            ToolPath = _directory.Combine("IntuneWinAppUtil.exe");
            File.WriteAllText(ToolPath, "stand-in for the packaging tool");
            Installer = _directory.Combine("drop", "setup.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(Installer));
            File.WriteAllBytes(Installer, new byte[] { 0x4D, 0x5A, 1, 2, 3 });
        }

        public string BaseFolder { get; private set; }

        public string TemplateDirectory { get; private set; }

        public string ToolPath { get; private set; }

        public string Installer { get; private set; }

        public void Dispose()
        {
            _directory.Dispose();
        }

        private static PackageVersionConfig ExeConfiguration(string version = "12.0.3")
        {
            var config = PackageVersionConfig.CreateDefault(null, InstallerType.Exe);
            config.Identity.SoftwareName = "Fabrikam Editor";
            config.Identity.Manufacturer = "Fabrikam";
            config.Identity.TargetVersion = version;
            config.Source.InstallerRelativePath = "setup.exe";
            config.Install.Arguments = "/quiet /norestart";
            config.Uninstall.ExecutablePath = "C:\\Program Files\\Fabrikam\\uninstall.exe";
            config.Uninstall.Arguments = "/quiet";
            config.Detection.Path = "C:\\Program Files\\Fabrikam\\editor.exe";
            config.Detection.MinimumVersion = version;
            return config;
        }

        private NewPackageRequest Request(PackageVersionConfig config = null, string installer = null, string language = "en")
        {
            return new NewPackageRequest
            {
                Item = new DroppedItem(DroppedKind.Exe, installer ?? Installer),
                Configuration = config ?? ExeConfiguration(),
                Language = language,
                RuntimeTemplateDirectory = TemplateDirectory,
                ContentPrepToolPath = ToolPath,
                WorkRoot = _directory.Combine("work")
            };
        }

        private NewPackageWorkflow Workflow(FakeRunner runner = null)
        {
            return new NewPackageWorkflow(BaseFolder, runner ?? new FakeRunner());
        }

        private BuildOutcome Run(NewPackageWorkflow workflow, NewPackageRequest request)
        {
            return workflow.Run(request, null, CancellationToken.None);
        }

        [Fact]
        public void TheProjectIsCreatedOnlyNowAndTheBuildIsDescribedForTheResultPage()
        {
            Assert.Empty(Directory.GetFileSystemEntries(BaseFolder));

            var outcome = Run(Workflow(), Request());

            Assert.Equal("fabrikam-editor", outcome.ProjectId);
            Assert.True(outcome.ProjectWasCreated);
            Assert.Equal("Fabrikam Editor", outcome.SoftwareName);
            Assert.Equal("12.0.3", outcome.TargetVersion);
            Assert.True(File.Exists(Path.Combine(BaseFolder, "fabrikam-editor", "project.json")));
            Assert.True(File.Exists(Path.Combine(BaseFolder, "fabrikam-editor", "versions", "12.0.3", "configuration.json")));
            Assert.True(File.Exists(Path.Combine(BaseFolder, "fabrikam-editor", "versions", "12.0.3", "source", "setup.exe")));
            Assert.True(File.Exists(outcome.PackagePath));
            Assert.True(File.Exists(outcome.GuidePath));
            Assert.True(File.Exists(outcome.LogPath));
            Assert.StartsWith(Path.Combine(BaseFolder, "fabrikam-editor", "versions", "12.0.3", "builds", outcome.BuildId), outcome.BuildDirectory);
            Assert.Equal("Install.cmd", outcome.InstallCommand);
            Assert.Equal("Install.cmd -DeploymentType Uninstall", outcome.UninstallCommand);
            Assert.Equal("System", outcome.InstallContext);
            Assert.Equal("Detect-App.ps1", outcome.DetectionScript);
            Assert.Equal("C:\\Program Files\\Fabrikam\\editor.exe", outcome.DetectionTarget);
        }

        [Fact]
        public void TheCopiedValuesAreThoseOfTheBuildThatJustFinished()
        {
            var workflow = Workflow();
            Run(workflow, Request(ExeConfiguration("12.0.3")));

            var second = Run(workflow, Request(ExeConfiguration("12.1.0")));

            Assert.Contains("app.version: 12.1.0", second.ClipboardText);
            Assert.DoesNotContain("app.version: 12.0.3", second.ClipboardText);
            Assert.Contains("buildId: " + second.BuildId, second.ClipboardText);
            Assert.Contains("program.installCommand: Install.cmd", second.ClipboardText);
        }

        [Fact]
        public void BuildingAgainReusesTheProjectAndTheVersionAndCreatesANewBuild()
        {
            var workflow = Workflow();
            var first = Run(workflow, Request());

            var second = Run(workflow, Request());

            Assert.False(second.ProjectWasCreated);
            Assert.NotEqual(first.BuildId, second.BuildId);
            Assert.Single(Directory.GetDirectories(Path.Combine(BaseFolder, "fabrikam-editor", "versions")));
            Assert.Equal(2, Directory.GetDirectories(Path.Combine(BaseFolder, "fabrikam-editor", "versions", "12.0.3", "builds")).Length);
            Assert.True(File.Exists(first.PackagePath));
        }

        [Fact]
        public void ANewVersionOfAnExistingProjectGetsItsOwnFolder()
        {
            var workflow = Workflow();
            Run(workflow, Request(ExeConfiguration("12.0.3")));
            var other = Path.Combine(_directory.Path, "drop", "other", "setup.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(other));
            File.WriteAllBytes(other, new byte[] { 0x4D, 0x5A, 9, 9 });

            var outcome = Run(workflow, Request(ExeConfiguration("12.1.0"), other));

            Assert.False(outcome.ProjectWasCreated);
            var versions = Directory.GetDirectories(Path.Combine(BaseFolder, "fabrikam-editor", "versions")).Select(Path.GetFileName).OrderBy(n => n).ToList();
            Assert.Equal(new[] { "12.0.3", "12.1.0" }, versions);
        }

        [Fact]
        public void AnExeWithoutVendorValuesIsRefusedAndNothingIsCreated()
        {
            var config = ExeConfiguration();
            config.Install.Arguments = null;
            config.Detection.Path = null;
            var runner = new FakeRunner();

            var exception = Assert.Throws<WorkflowException>(() => Run(Workflow(runner), Request(config)));

            Assert.Equal(WorkflowProblem.ConfigurationInvalid, exception.Problem);
            var fields = exception.Issues.Select(i => i.Field).ToList();
            Assert.Contains("install.arguments", fields);
            Assert.Contains("detection.path", fields);
            Assert.Empty(Directory.GetFileSystemEntries(BaseFolder));
            Assert.Equal(0, runner.Calls);
        }

        [Fact]
        public void WithoutAToolNothingIsCreated()
        {
            var request = Request();
            request.ContentPrepToolPath = null;

            Assert.Equal(WorkflowProblem.ToolNotConfigured, Assert.Throws<WorkflowException>(() => Run(Workflow(), request)).Problem);

            request.ContentPrepToolPath = _directory.Combine("absent.exe");
            Assert.Equal(WorkflowProblem.ToolNotConfigured, Assert.Throws<WorkflowException>(() => Run(Workflow(), request)).Problem);
            Assert.Empty(Directory.GetFileSystemEntries(BaseFolder));
        }

        [Fact]
        public void AnInvalidProjectIdIsRefused()
        {
            var request = Request();
            request.RequestedProjectId = "CON";

            Assert.Equal(WorkflowProblem.InvalidProjectId, Assert.Throws<WorkflowException>(() => Run(Workflow(), request)).Problem);
            Assert.Empty(Directory.GetFileSystemEntries(BaseFolder));
        }

        [Fact]
        public void AnAdvancedModeProjectIdAndDisplayNameAreUsed()
        {
            var request = Request();
            request.RequestedProjectId = "editor-pro";
            request.DisplayName = "Editor Pro (Fabrikam)";

            var outcome = Run(Workflow(), request);

            Assert.Equal("editor-pro", outcome.ProjectId);
            Assert.Equal("Editor Pro (Fabrikam)", new ProjectStore(BaseFolder).Load("editor-pro").DisplayName);
        }

        [Fact]
        public void ARefusedSourceLeavesNoEmptyProjectBehind()
        {
            var request = Request(installer: _directory.Combine("drop", "missing.exe"));

            var exception = Assert.Throws<WorkflowException>(() => Run(Workflow(), request));

            Assert.Equal(WorkflowProblem.SourceRejected, exception.Problem);
            Assert.Equal(ImportProblem.NotFound, exception.ImportProblem);
            Assert.Empty(Directory.GetFileSystemEntries(BaseFolder));
        }

        [Fact]
        public void ADifferentInstallerForAnExistingVersionIsRefusedAndTheStoredOneStaysUntouched()
        {
            var workflow = Workflow();
            Run(workflow, Request());
            var other = Path.Combine(_directory.Path, "drop", "other", "setup.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(other));
            File.WriteAllBytes(other, new byte[] { 0x4D, 0x5A, 7, 7, 7 });
            var stored = Path.Combine(BaseFolder, "fabrikam-editor", "versions", "12.0.3", "source", "setup.exe");
            var before = File.ReadAllBytes(stored);

            var exception = Assert.Throws<WorkflowException>(() => Run(workflow, Request(installer: other)));

            Assert.Equal(WorkflowProblem.SourceAlreadyStored, exception.Problem);
            Assert.Equal(before, File.ReadAllBytes(stored));
        }

        [Fact]
        public void AFailedBuildKeepsProjectAndVersionSoTheUserCanTryAgain()
        {
            var failing = new FakeRunner { FailWith = new ContentPrepException(ContentPrepProblem.ToolFailed, "exit code 5") };
            var workflow = Workflow(failing);

            var exception = Assert.Throws<BuildFailedException>(() => Run(workflow, Request()));

            Assert.Equal(BuildProblem.PackagingFailed, exception.Problem);
            Assert.True(File.Exists(Path.Combine(BaseFolder, "fabrikam-editor", "project.json")));
            var published = Directory.GetDirectories(Path.Combine(BaseFolder, "fabrikam-editor", "versions", "12.0.3", "builds"))
                .Where(d => !d.EndsWith(".partial", StringComparison.Ordinal))
                .ToList();
            Assert.Empty(published);

            var retry = Run(Workflow(new FakeRunner()), Request());
            Assert.False(retry.ProjectWasCreated);
            Assert.True(File.Exists(retry.PackagePath));
        }

        [Fact]
        public void TheGuideFollowsTheLanguageOfTheRequest()
        {
            var outcome = Run(Workflow(), Request(language: "de"));

            Assert.Contains("<html lang=\"de\">", File.ReadAllText(outcome.GuidePath));
        }

        [Fact]
        public void ProgressIsReportedPhaseByPhase()
        {
            var phases = new System.Collections.Generic.List<BuildPhase>();

            Workflow().Run(Request(), new Progress(p => phases.Add(p.Phase)), CancellationToken.None);

            Assert.Equal(BuildPhase.ValidateConfiguration, phases.First());
            Assert.Equal(BuildPhase.Cleanup, phases.Last());
        }

        private sealed class Progress : IProgress<BuildProgress>
        {
            private readonly Action<BuildProgress> _action;

            public Progress(Action<BuildProgress> action)
            {
                _action = action;
            }

            public void Report(BuildProgress value)
            {
                _action(value);
            }
        }
    }
}
