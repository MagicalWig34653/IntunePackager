using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IntunePackageBuilder.Build.Packaging;
using IntunePackageBuilder.Build.Pipeline;
using IntunePackageBuilder.Core.Builds;
using Xunit;

namespace IntunePackageBuilder.Build.Tests
{
    public class BuildPipelineTests
    {
        private static BuildFailedException Fails(Func<BuildResult> action)
        {
            return Assert.Throws<BuildFailedException>(action);
        }

        [Fact]
        public void ASuccessfulBuildPublishesEverythingInOneBuildFolder()
        {
            using (var fixture = new PipelineFixture())
            {
                var result = fixture.Build(new FakeRunner());

                Assert.True(BuildId.IsValid(result.BuildId));
                Assert.Equal(Path.Combine(fixture.BuildsDirectory, result.BuildId), result.BuildDirectory);
                Assert.True(File.Exists(Path.Combine(result.BuildDirectory, "contoso-reader.intunewin")));
                foreach (var name in new[] { "Detect-App.ps1", "Einrichtung.html", "Einstellungen.json", "Einstellungen.csv" })
                {
                    Assert.True(File.Exists(Path.Combine(result.BuildDirectory, "intune", name)), name);
                }

                Assert.True(File.Exists(Path.Combine(result.BuildDirectory, BuildSnapshot.FileName)));
                Assert.True(File.Exists(Path.Combine(result.BuildDirectory, BuildManifest.FileName)));
                Assert.True(File.Exists(Path.Combine(result.BuildDirectory, "build.log")));
                Assert.Contains(result.PackagePath, result.Files);
                Assert.Equal(new[] { result.BuildId }, fixture.PublishedBuilds());
            }
        }

        [Fact]
        public void NothingIsLeftBehindInTheWorkFolderOrAsAnUnfinishedBuild()
        {
            using (var fixture = new PipelineFixture())
            {
                fixture.Build(new FakeRunner());

                Assert.Empty(Directory.GetFileSystemEntries(fixture.WorkRoot));
                Assert.Empty(Directory.GetDirectories(fixture.BuildsDirectory, "*.partial"));
                Assert.False(File.Exists(Path.Combine(fixture.VersionDirectory, ".lock")), "the version lock is released");
            }
        }

        [Fact]
        public void ThePackageContentHasTheRuntimeTemplatesTheConfigurationAndTheStoredSource()
        {
            using (var fixture = new PipelineFixture())
            {
                var runner = new FakeRunner();
                fixture.Build(runner);

                Assert.Equal(
                    new[] { "Deploy-Wrapper.ps1", "Deployment.config.json", "Files/setup.msi", "Files/vendor.txt", "Install.cmd" },
                    runner.StagedFiles);
                Assert.Equal("Install.cmd", runner.LastRequest.SetupFile);
            }
        }

        [Fact]
        public void TheDetectionScriptAndTheGuideStayOutOfThePackage()
        {
            using (var fixture = new PipelineFixture())
            {
                var runner = new FakeRunner();
                fixture.Build(runner);

                Assert.DoesNotContain(runner.StagedFiles, f => f.IndexOf("Detect-App", StringComparison.Ordinal) >= 0 || f.EndsWith(".html", StringComparison.Ordinal));
            }
        }

        [Fact]
        public void TheManifestDescribesTheBuildAndMatchesTheFiles()
        {
            using (var fixture = new PipelineFixture())
            {
                var result = fixture.Build(new FakeRunner());
                var manifest = BuildManifestStore.Read(Path.Combine(result.BuildDirectory, BuildManifest.FileName));

                Assert.Equal(result.BuildId, manifest.BuildId);
                Assert.Equal("contoso-reader", manifest.ProjectId);
                Assert.Equal("1.0.0", manifest.SoftwareVersion);
                Assert.Equal(fixture.Manifest.ComputeFingerprint(), manifest.SourceFingerprint);
                Assert.Equal("contoso-reader.intunewin", manifest.Package.Path);
                Assert.Equal(ContentPrepTool.ComputeSha256(result.PackagePath), manifest.Package.Sha256);
                Assert.Equal(new FileInfo(result.PackagePath).Length, manifest.Package.Size);
                Assert.Contains(manifest.Files, f => f.Path == "intune/Detect-App.ps1");
                Assert.Contains(manifest.Files, f => f.Path == BuildSnapshot.FileName);
                Assert.DoesNotContain(manifest.Files, f => f.Path == BuildManifest.FileName || f.Path == "build.log");
                foreach (var entry in manifest.Files)
                {
                    var path = Path.Combine(result.BuildDirectory, entry.Path.Replace('/', Path.DirectorySeparatorChar));
                    Assert.Equal(ContentPrepTool.ComputeSha256(path), entry.Sha256);
                    Assert.Equal(new FileInfo(path).Length, entry.Size);
                }
            }
        }

        [Fact]
        public void TheSnapshotKeepsTheConfigurationLanguageAndFingerprintOfTheBuild()
        {
            using (var fixture = new PipelineFixture())
            {
                var result = fixture.Build(new FakeRunner(), "de");
                var snapshot = BuildSnapshotStore.Read(Path.Combine(result.BuildDirectory, BuildSnapshot.FileName));
                fixture.Configuration.Identity.SoftwareName = "Changed later";

                Assert.Equal(result.BuildId, snapshot.BuildId);
                Assert.Equal("de", snapshot.Language);
                Assert.Equal("Contoso Reader", snapshot.Configuration.Identity.SoftwareName);
                Assert.Equal(fixture.Manifest.ComputeFingerprint(), snapshot.SourceFingerprint);
                Assert.Contains("<html lang=\"de\">", File.ReadAllText(Path.Combine(result.IntuneDirectory, "Einrichtung.html")));
            }
        }

        [Fact]
        public void TheToolIsRecordedInTheManifestAndTheLog()
        {
            using (var fixture = new PipelineFixture())
            {
                var tool = Path.Combine(fixture.Root, "tool.exe");
                File.WriteAllText(tool, "not the real tool");
                var request = fixture.Request();
                request.ContentPrepToolPath = tool;

                var result = new BuildPipeline(new FakeRunner()).Run(request, null, CancellationToken.None);
                var manifest = BuildManifestStore.Read(Path.Combine(result.BuildDirectory, BuildManifest.FileName));

                Assert.Equal(ContentPrepTool.ComputeSha256(tool), manifest.ContentPrepToolSha256);
                Assert.Null(manifest.ContentPrepToolVersion);
                Assert.Contains("SHA-256 " + manifest.ContentPrepToolSha256, File.ReadAllText(Path.Combine(result.BuildDirectory, "build.log")));
            }
        }

        [Fact]
        public void BuildingTheSameVersionAgainCreatesANewBuildAndKeepsTheOldOneUntouched()
        {
            using (var fixture = new PipelineFixture())
            {
                var first = fixture.Build(new FakeRunner());
                var before = first.Files.ToDictionary(f => f, ContentPrepTool.ComputeSha256);

                var second = fixture.Build(new FakeRunner());

                Assert.NotEqual(first.BuildId, second.BuildId);
                Assert.Equal(2, fixture.PublishedBuilds().Count);
                foreach (var pair in before)
                {
                    Assert.Equal(pair.Value, ContentPrepTool.ComputeSha256(pair.Key));
                }

                var one = BuildManifestStore.Read(Path.Combine(first.BuildDirectory, BuildManifest.FileName));
                var two = BuildManifestStore.Read(Path.Combine(second.BuildDirectory, BuildManifest.FileName));
                Assert.Equal(one.SoftwareVersion, two.SoftwareVersion);
                Assert.Equal(one.SourceFingerprint, two.SourceFingerprint);
            }
        }

        [Fact]
        public void TwoBuildsAtTheSameInstantGetDifferentBuildIds()
        {
            using (var fixture = new PipelineFixture())
            {
                var instant = new DateTime(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc);
                var pipeline = new BuildPipeline(new FakeRunner(), () => instant);

                var ids = Enumerable.Range(0, 5).Select(i => pipeline.Run(fixture.Request(), null, CancellationToken.None).BuildId).ToList();

                Assert.Equal(5, ids.Distinct().Count());
            }
        }

        [Fact]
        public void AModifiedStoredSourceIsRefusedBeforeAnythingIsBuilt()
        {
            using (var fixture = new PipelineFixture())
            {
                File.WriteAllText(Path.Combine(fixture.SourceDirectory, "vendor.txt"), "tampered");
                var runner = new FakeRunner();

                var exception = Fails(() => fixture.Build(runner));

                Assert.Equal(BuildProblem.SourceChanged, exception.Problem);
                Assert.Equal(BuildPhase.VerifySource, exception.Phase);
                Assert.Contains("vendor.txt", exception.Detail);
                Assert.Equal(0, runner.Calls);
                Assert.Empty(fixture.PublishedBuilds());
            }
        }

        [Fact]
        public void AnAddedOrMissingSourceFileIsRefused()
        {
            using (var fixture = new PipelineFixture())
            {
                File.WriteAllText(Path.Combine(fixture.SourceDirectory, "added.txt"), "x");
                Assert.Equal(BuildProblem.SourceChanged, Fails(() => fixture.Build(new FakeRunner())).Problem);
                File.Delete(Path.Combine(fixture.SourceDirectory, "added.txt"));
                File.Delete(Path.Combine(fixture.SourceDirectory, "vendor.txt"));
                Assert.Equal(BuildProblem.SourceChanged, Fails(() => fixture.Build(new FakeRunner())).Problem);
            }
        }

        [Fact]
        public void AMissingManifestOrAnInstallerOutsideTheManifestIsRefused()
        {
            using (var fixture = new PipelineFixture())
            {
                fixture.Configuration.Source.InstallerRelativePath = "other.msi";
                Assert.Equal(BuildProblem.SourceMissing, Fails(() => fixture.Build(new FakeRunner())).Problem);

                fixture.Configuration.Source.InstallerRelativePath = "setup.msi";
                File.Delete(Path.Combine(fixture.VersionDirectory, "source-manifest.json"));
                Assert.Equal(BuildProblem.SourceMissing, Fails(() => fixture.Build(new FakeRunner())).Problem);
            }
        }

        [Fact]
        public void ABuildReportsItsPhasesInOrderWithoutAPercentage()
        {
            using (var fixture = new PipelineFixture())
            {
                var phases = new List<BuildPhase>();
                var progress = new SynchronousProgress(p => phases.Add(p.Phase));

                new BuildPipeline(new FakeRunner()).Run(fixture.Request(), progress, CancellationToken.None);

                Assert.Equal(
                    new[]
                    {
                        BuildPhase.ValidateConfiguration, BuildPhase.AcquireLock, BuildPhase.VerifySource, BuildPhase.PrepareWorkspace,
                        BuildPhase.StagePackage, BuildPhase.GenerateArtifacts, BuildPhase.PackContent, BuildPhase.VerifyPackage,
                        BuildPhase.Publish, BuildPhase.Cleanup
                    },
                    phases);
            }
        }

        [Fact]
        public void ARunningBuildBlocksASecondBuildOfTheSameVersionAndDoesNotBlockTheCaller()
        {
            using (var fixture = new PipelineFixture())
            using (var entered = new ManualResetEventSlim(false))
            using (var release = new ManualResetEventSlim(false))
            {
                var runner = new FakeRunner
                {
                    OnPack = request =>
                    {
                        entered.Set();
                        release.Wait(TimeSpan.FromSeconds(60));
                    }
                };

                var running = new BuildPipeline(runner).RunAsync(fixture.Request(), null, CancellationToken.None);
                Assert.True(entered.Wait(TimeSpan.FromSeconds(60)), "the build did not reach the packaging step");
                Assert.False(running.IsCompleted, "RunAsync must return while the build is still running");

                var second = Fails(() => fixture.Build(new FakeRunner()));
                Assert.Equal(BuildProblem.VersionLocked, second.Problem);

                release.Set();
                Assert.True(running.Wait(TimeSpan.FromSeconds(60)));
                Assert.Single(fixture.PublishedBuilds());
            }
        }

        [Fact]
        public void ABuildOfAnotherVersionIsNotBlocked()
        {
            using (var one = new PipelineFixture())
            using (var two = new PipelineFixture())
            using (var entered = new ManualResetEventSlim(false))
            using (var release = new ManualResetEventSlim(false))
            {
                var blocking = new FakeRunner { OnPack = request => { entered.Set(); release.Wait(TimeSpan.FromSeconds(60)); } };
                var running = new BuildPipeline(blocking).RunAsync(one.Request(), null, CancellationToken.None);
                Assert.True(entered.Wait(TimeSpan.FromSeconds(60)));

                var other = two.Build(new FakeRunner());

                Assert.True(Directory.Exists(other.BuildDirectory));
                release.Set();
                Assert.True(running.Wait(TimeSpan.FromSeconds(60)));
            }
        }

        [Fact]
        public void CancellingStopsTheBuildAtTheNextStepAndPublishesNothing()
        {
            using (var fixture = new PipelineFixture())
            using (var cancellation = new CancellationTokenSource())
            {
                var runner = new FakeRunner { OnPack = request => cancellation.Cancel() };

                var exception = Fails(() => new BuildPipeline(runner).Run(fixture.Request(), null, cancellation.Token));

                Assert.Equal(BuildProblem.Cancelled, exception.Problem);
                Assert.Empty(fixture.PublishedBuilds());
                Assert.Empty(Directory.GetFileSystemEntries(fixture.WorkRoot));
            }
        }

        [Fact]
        public void APackagingFailurePublishesNothingKeepsTheCauseAndWritesAFailureLog()
        {
            using (var fixture = new PipelineFixture())
            {
                var runner = new FakeRunner { FailWith = new ContentPrepException(ContentPrepProblem.ToolFailed, "exit code 5") };

                var exception = Fails(() => fixture.Build(runner));

                Assert.Equal(BuildProblem.PackagingFailed, exception.Problem);
                Assert.Equal(BuildPhase.PackContent, exception.Phase);
                Assert.Equal(ContentPrepProblem.ToolFailed, ((ContentPrepException)exception.InnerException).Problem);
                Assert.Empty(fixture.PublishedBuilds());
                Assert.Empty(Directory.GetDirectories(fixture.BuildsDirectory, "*.partial"));
                Assert.Empty(Directory.GetFileSystemEntries(fixture.WorkRoot));
                var log = Path.Combine(fixture.BuildsDirectory, exception.BuildId + ".failed.log");
                Assert.True(File.Exists(log));
                Assert.Contains("ToolFailed", File.ReadAllText(log));
            }
        }

        [Fact]
        public void AnInvalidPackageFromTheToolIsCaughtByThePipelineToo()
        {
            using (var fixture = new PipelineFixture())
            {
                // A faulty tool wrapper could return a broken file as if it were a package.
                var exception = Fails(() => new BuildPipeline(new BrokenOutputRunner()).Run(fixture.Request(), null, CancellationToken.None));

                Assert.Equal(BuildProblem.PackagingFailed, exception.Problem);
                Assert.Equal(BuildPhase.VerifyPackage, exception.Phase);
                Assert.Empty(fixture.PublishedBuilds());
            }
        }

        [Fact]
        public void AnInvalidConfigurationIsRefusedWithoutTouchingTheVersionFolder()
        {
            using (var fixture = new PipelineFixture())
            {
                fixture.Configuration.Identity.SoftwareName = " ";

                var exception = Fails(() => fixture.Build(new FakeRunner()));

                Assert.Equal(BuildProblem.ConfigurationInvalid, exception.Problem);
                Assert.Contains("identity.softwareName", exception.Detail);
                Assert.False(Directory.Exists(fixture.BuildsDirectory));
            }
        }

        [Fact]
        public void MissingRuntimeTemplatesAreReported()
        {
            using (var fixture = new PipelineFixture())
            {
                File.Delete(Path.Combine(fixture.TemplateDirectory, "Install.cmd"));

                Assert.Equal(BuildProblem.TemplateMissing, Fails(() => fixture.Build(new FakeRunner())).Problem);
            }
        }

        [Fact]
        public void AnUnknownLanguageOrMissingVersionFolderIsAnInvalidRequest()
        {
            using (var fixture = new PipelineFixture())
            {
                var request = fixture.Request("fr");
                Assert.Equal(BuildProblem.RequestInvalid, Fails(() => new BuildPipeline(new FakeRunner()).Run(request, null, CancellationToken.None)).Problem);

                request = fixture.Request();
                request.VersionDirectory = Path.Combine(fixture.Root, "absent");
                Assert.Equal(BuildProblem.RequestInvalid, Fails(() => new BuildPipeline(new FakeRunner()).Run(request, null, CancellationToken.None)).Problem);
            }
        }

        [Fact]
        public void AnUnwritableBuildFolderIsReportedBeforeAnythingElseHappens()
        {
            using (var fixture = new PipelineFixture())
            {
                // A file named like the builds folder makes creating the folder fail, like a folder without write access.
                File.WriteAllText(fixture.BuildsDirectory, "in the way");
                var runner = new FakeRunner();

                var exception = Fails(() => fixture.Build(runner));

                Assert.Equal(BuildProblem.NotWritable, exception.Problem);
                Assert.Equal(0, runner.Calls);
                Assert.Empty(Directory.GetFileSystemEntries(fixture.WorkRoot));
            }
        }

        [Fact]
        public void NotEnoughFreeDiskSpaceIsReportedWithoutBuilding()
        {
            using (var fixture = new PipelineFixture())
            {
                var request = fixture.Request();
                request.FreeSpaceProbe = path => 1024;
                var runner = new FakeRunner();

                var exception = Fails(() => new BuildPipeline(runner).Run(request, null, CancellationToken.None));

                Assert.Equal(BuildProblem.NotEnoughSpace, exception.Problem);
                Assert.Equal(0, runner.Calls);
                Assert.Empty(fixture.PublishedBuilds());
            }
        }

        [Fact]
        public void APathThatWouldExceedTheWindowsLimitIsReportedNotLostInACopyError()
        {
            using (var source = new PipelineFixture())
            {
                var deep = Path.Combine(source.SourceDirectory, new string('d', 60), new string('e', 60));
                Directory.CreateDirectory(deep);
                File.WriteAllText(Path.Combine(deep, "f.txt"), "x");
                File.Delete(Path.Combine(source.VersionDirectory, "source-manifest.json"));
                var manifest = IntunePackageBuilder.Core.Sources.SourceManifestBuilder.Build(source.SourceDirectory);
                IntunePackageBuilder.Core.Sources.SourceManifestStore.WriteNew(Path.Combine(source.VersionDirectory, "source-manifest.json"), manifest);

                var request = source.Request();
                request.WorkRoot = Path.Combine(source.Root, new string('w', 120));
                var runner = new FakeRunner();

                var exception = Fails(() => new BuildPipeline(runner).Run(request, null, CancellationToken.None));

                Assert.Equal(BuildProblem.PathTooLong, exception.Problem);
                Assert.Equal(BuildPhase.StagePackage, exception.Phase);
                Assert.Equal(0, runner.Calls);
            }
        }

        [Fact]
        public void CleanupRemovesOnlyUnfinishedBuildFoldersWithTheMarkerAndNeverUserFiles()
        {
            using (var fixture = new PipelineFixture())
            {
                var builds = fixture.BuildsDirectory;
                Directory.CreateDirectory(builds);
                var stale = Path.Combine(builds, "20260101-000000-aaaa.partial");
                Directory.CreateDirectory(stale);
                File.WriteAllText(Path.Combine(stale, BuildPipeline.PartialMarkerFileName), "20260101-000000-aaaa");
                File.WriteAllText(Path.Combine(stale, "junk.bin"), "junk");
                var foreign = Path.Combine(builds, "mine.partial");
                Directory.CreateDirectory(foreign);
                File.WriteAllText(Path.Combine(foreign, "keep.txt"), "user data");
                var foreignWork = Path.Combine(fixture.WorkRoot, "user-folder");
                Directory.CreateDirectory(foreignWork);
                File.WriteAllText(Path.Combine(foreignWork, "keep.txt"), "user data");

                fixture.Build(new FakeRunner());

                Assert.False(Directory.Exists(stale));
                Assert.True(File.Exists(Path.Combine(foreign, "keep.txt")));
                Assert.True(File.Exists(Path.Combine(foreignWork, "keep.txt")));
            }
        }

        [Fact]
        public void TheStoredSourceAndTheConfigurationFileAreNotChangedByABuild()
        {
            using (var fixture = new PipelineFixture())
            {
                var before = Directory.EnumerateFiles(fixture.SourceDirectory, "*", SearchOption.AllDirectories)
                    .ToDictionary(f => f, ContentPrepTool.ComputeSha256);

                fixture.Build(new FakeRunner());

                foreach (var pair in before)
                {
                    Assert.Equal(pair.Value, ContentPrepTool.ComputeSha256(pair.Key));
                }
            }
        }

        [Fact]
        public void AWorkspaceIsOnlyRemovedWhenItsMarkerMatches()
        {
            using (var directory = new TempDirectory())
            {
                var workspace = BuildWorkspace.Create(directory.Path, "20260101-000000-aaaa");
                File.WriteAllText(Path.Combine(workspace.Root, BuildWorkspace.MarkerFileName), "someone else");

                Assert.False(workspace.Cleanup());
                Assert.True(Directory.Exists(workspace.Root));

                File.WriteAllText(Path.Combine(workspace.Root, BuildWorkspace.MarkerFileName), "20260101-000000-aaaa");
                Assert.True(workspace.Cleanup());
                Assert.False(Directory.Exists(workspace.Root));
            }
        }

        [Fact]
        public void AWorkspaceThatAlreadyExistsIsNeverReused()
        {
            using (var directory = new TempDirectory())
            {
                Directory.CreateDirectory(Path.Combine(directory.Path, "20260101-000000-aaaa"));

                Assert.Throws<IOException>(() => BuildWorkspace.Create(directory.Path, "20260101-000000-aaaa"));
            }
        }

        [RealToolFact]
        public void ABuildWithTheRealToolProducesAVerifiedPackage()
        {
            using (var fixture = new PipelineFixture())
            {
                var request = fixture.Request();
                request.ContentPrepToolPath = Environment.GetEnvironmentVariable(RealToolFactAttribute.VariableName);

                var result = new BuildPipeline(new ContentPrepTool()).Run(request, null, CancellationToken.None);
                var manifest = BuildManifestStore.Read(Path.Combine(result.BuildDirectory, BuildManifest.FileName));

                IntunewinVerifier.Verify(result.PackagePath);
                Assert.Equal("1.8.7", manifest.ContentPrepToolVersion);
                Assert.Equal(ContentPrepTool.ComputeSha256(result.PackagePath), manifest.Package.Sha256);
            }
        }

        private sealed class SynchronousProgress : IProgress<BuildProgress>
        {
            private readonly Action<BuildProgress> _action;

            public SynchronousProgress(Action<BuildProgress> action)
            {
                _action = action;
            }

            public void Report(BuildProgress value)
            {
                _action(value);
            }
        }

        private sealed class BrokenOutputRunner : IContentPrepRunner
        {
            public string Pack(PackRequest request)
            {
                Directory.CreateDirectory(request.OutputFolder);
                var output = Path.Combine(request.OutputFolder, "Install.intunewin");
                File.WriteAllText(output, "this is not a package");
                return output;
            }
        }
    }
}
