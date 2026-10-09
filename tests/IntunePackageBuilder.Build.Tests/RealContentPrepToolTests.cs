using System;
using System.IO;
using IntunePackageBuilder.Build.Packaging;
using Xunit;

namespace IntunePackageBuilder.Build.Tests
{
    /// <summary>Runs only where <c>IPB_CONTENT_PREP_TOOL</c> points to the real tool (the CI downloads and verifies it); otherwise it is reported as skipped.</summary>
    public sealed class RealToolFactAttribute : FactAttribute
    {
        public const string VariableName = "IPB_CONTENT_PREP_TOOL";

        public RealToolFactAttribute()
        {
            var path = Environment.GetEnvironmentVariable(VariableName);
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                Skip = VariableName + " does not point to the Win32 Content Prep Tool.";
            }
        }
    }

    public class RealContentPrepToolTests
    {
        private static string ToolPath
        {
            get { return Environment.GetEnvironmentVariable(RealToolFactAttribute.VariableName); }
        }

        [RealToolFact]
        public void TheToolInTheEnvironmentIsAKnownVersion()
        {
            Assert.Equal("1.8.7", ContentPrepTool.IdentifyVersion(ToolPath));
        }

        [RealToolFact]
        public void TheRealToolPacksAFolderIntoAVerifiedPackage()
        {
            using (var directory = new TempDirectory())
            {
                var setup = directory.Combine("setup");
                Directory.CreateDirectory(Path.Combine(setup, "Files"));
                File.WriteAllText(Path.Combine(setup, "Install.cmd"), "@echo off\r\nexit /b 0\r\n");
                File.WriteAllText(Path.Combine(setup, "Files", "readme.txt"), "payload");

                var produced = new ContentPrepTool().Pack(new PackRequest
                {
                    ToolPath = ToolPath,
                    SetupFolder = setup,
                    SetupFile = "Install.cmd",
                    OutputFolder = directory.Combine("out"),
                    Timeout = TimeSpan.FromMinutes(5)
                });

                Assert.Equal(directory.Combine("out", "Install.intunewin"), produced);
                Assert.True(new FileInfo(produced).Length > 0);
            }
        }

        [RealToolFact]
        public void TheRealToolFailsForAMissingSetupFileWithoutProducingAPackage()
        {
            using (var directory = new TempDirectory())
            {
                Directory.CreateDirectory(directory.Combine("setup"));

                var exception = Assert.Throws<ContentPrepException>(() => new ContentPrepTool().Pack(new PackRequest
                {
                    ToolPath = ToolPath,
                    SetupFolder = directory.Combine("setup"),
                    SetupFile = "Install.cmd",
                    OutputFolder = directory.Combine("out")
                }));

                Assert.Equal(ContentPrepProblem.InputMissing, exception.Problem);
            }
        }
    }
}
