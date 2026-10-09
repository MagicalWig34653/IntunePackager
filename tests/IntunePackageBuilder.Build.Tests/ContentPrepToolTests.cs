using System;
using System.IO;
using System.Text;
using IntunePackageBuilder.Build.Packaging;
using Xunit;

namespace IntunePackageBuilder.Build.Tests
{
    public class ContentPrepToolTests
    {
        private static PackRequest Request(TempDirectory directory, string tool, bool allowUnknown = true)
        {
            var setup = directory.Combine("setup");
            Directory.CreateDirectory(setup);
            File.WriteAllText(Path.Combine(setup, "Install.cmd"), "@echo off\r\n");
            return new PackRequest
            {
                ToolPath = tool,
                SetupFolder = setup,
                SetupFile = "Install.cmd",
                OutputFolder = directory.Combine("out"),
                AllowUnknownTool = allowUnknown,
                Timeout = TimeSpan.FromSeconds(60)
            };
        }

        private static string PreparedPackage(TempDirectory directory)
        {
            var path = directory.Combine("prepared.intunewin");
            TestPackages.WriteIntunewin(path);
            return path;
        }

        [Fact]
        public void ThePackageProducedByTheToolIsVerifiedAndReturned()
        {
            using (var directory = new TempDirectory())
            {
                var tool = TestPackages.WriteFakeTool(directory.Combine("tool.cmd"), PreparedPackage(directory));

                var produced = new ContentPrepTool().Pack(Request(directory, tool));

                Assert.Equal(directory.Combine("out", "Install.intunewin"), produced);
                Assert.True(File.Exists(produced));
            }
        }

        [Fact]
        public void APathWithSpacesAndQuotesReachesTheToolIntact()
        {
            using (var directory = new TempDirectory())
            {
                var tool = TestPackages.WriteFakeTool(directory.Combine("my tool.cmd"), PreparedPackage(directory));
                var request = Request(directory, tool);
                var spaced = directory.Combine("set up folder");
                Directory.Move(request.SetupFolder, spaced);
                request.SetupFolder = spaced;
                request.OutputFolder = directory.Combine("out folder");

                var produced = new ContentPrepTool().Pack(request);

                Assert.Equal(Path.Combine(request.OutputFolder, "Install.intunewin"), produced);
            }
        }

        [Fact]
        public void ANonZeroExitCodeIsReportedWithTheOutput()
        {
            using (var directory = new TempDirectory())
            {
                var tool = TestPackages.WriteFakeTool(directory.Combine("tool.cmd"), PreparedPackage(directory), exitCode: 7, copyOutput: false);

                var exception = Assert.Throws<ContentPrepException>(() => new ContentPrepTool().Pack(Request(directory, tool)));

                Assert.Equal(ContentPrepProblem.ToolFailed, exception.Problem);
                Assert.Contains("exit code 7", exception.Detail);
                Assert.Contains("tool says no", exception.Detail);
            }
        }

        [Fact]
        public void SuccessWithoutAnOutputFileIsAnError()
        {
            using (var directory = new TempDirectory())
            {
                var tool = TestPackages.WriteFakeTool(directory.Combine("tool.cmd"), PreparedPackage(directory), copyOutput: false);

                Assert.Equal(ContentPrepProblem.OutputMissing, Assert.Throws<ContentPrepException>(() => new ContentPrepTool().Pack(Request(directory, tool))).Problem);
            }
        }

        [Fact]
        public void AnInvalidOutputFileIsAnError()
        {
            using (var directory = new TempDirectory())
            {
                var broken = directory.Combine("broken.intunewin");
                File.WriteAllText(broken, "not a package");
                var tool = TestPackages.WriteFakeTool(directory.Combine("tool.cmd"), broken);

                Assert.Equal(ContentPrepProblem.OutputInvalid, Assert.Throws<ContentPrepException>(() => new ContentPrepTool().Pack(Request(directory, tool))).Problem);
            }
        }

        [Fact]
        public void AToolThatRunsTooLongIsStopped()
        {
            using (var directory = new TempDirectory())
            {
                var tool = TestPackages.WriteFakeTool(directory.Combine("tool.cmd"), PreparedPackage(directory), sleep: true);
                var request = Request(directory, tool);
                request.Timeout = TimeSpan.FromSeconds(1);

                Assert.Equal(ContentPrepProblem.ToolTimedOut, Assert.Throws<ContentPrepException>(() => new ContentPrepTool().Pack(request)).Problem);
            }
        }

        [Fact]
        public void AnUnknownToolIsRefusedUnlessConfirmed()
        {
            using (var directory = new TempDirectory())
            {
                var tool = TestPackages.WriteFakeTool(directory.Combine("tool.cmd"), PreparedPackage(directory));

                var exception = Assert.Throws<ContentPrepException>(() => new ContentPrepTool().Pack(Request(directory, tool, allowUnknown: false)));

                Assert.Equal(ContentPrepProblem.ToolNotRecognized, exception.Problem);
                Assert.Contains(ContentPrepTool.ComputeSha256(tool), exception.Detail);
                Assert.False(Directory.Exists(directory.Combine("out")), "nothing is created when the tool is refused");
            }
        }

        [Fact]
        public void MissingToolAndMissingInputsAreReported()
        {
            using (var directory = new TempDirectory())
            {
                var tool = TestPackages.WriteFakeTool(directory.Combine("tool.cmd"), PreparedPackage(directory));
                var request = Request(directory, directory.Combine("absent.exe"));
                Assert.Equal(ContentPrepProblem.ToolMissing, Assert.Throws<ContentPrepException>(() => new ContentPrepTool().Pack(request)).Problem);

                request = Request(directory, tool);
                request.ToolPath = null;
                Assert.Equal(ContentPrepProblem.ToolMissing, Assert.Throws<ContentPrepException>(() => new ContentPrepTool().Pack(request)).Problem);

                request = Request(directory, tool);
                request.SetupFile = "missing.cmd";
                Assert.Equal(ContentPrepProblem.InputMissing, Assert.Throws<ContentPrepException>(() => new ContentPrepTool().Pack(request)).Problem);

                request = Request(directory, tool);
                request.SetupFolder = directory.Combine("no-such-folder");
                Assert.Equal(ContentPrepProblem.InputMissing, Assert.Throws<ContentPrepException>(() => new ContentPrepTool().Pack(request)).Problem);
            }
        }

        [Fact]
        public void ThePinnedVersionHasAValidSha256()
        {
            var version = Assert.Single(ContentPrepTool.KnownVersions);

            Assert.Equal("1.8.7", version.Version);
            Assert.Matches("^[0-9a-f]{64}$", version.Sha256);
        }

        [Fact]
        public void TheSha256IsComputedOverTheFileContent()
        {
            using (var directory = new TempDirectory())
            {
                var path = directory.Combine("abc.bin");
                File.WriteAllBytes(path, Encoding.ASCII.GetBytes("abc"));

                Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", ContentPrepTool.ComputeSha256(path));
                Assert.Null(ContentPrepTool.IdentifyVersion(path));
            }
        }
    }
}
