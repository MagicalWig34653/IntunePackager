using System;
using System.IO;
using System.Threading;
using IntunePackageBuilder.Build.Packaging;
using IntunePackageBuilder.Build.Pipeline;
using IntunePackageBuilder.Build.Processes;
using IntunePackageBuilder.Core.Versions;
using Xunit;

namespace IntunePackageBuilder.Build.Tests
{
    /// <summary>
    /// The contract between the build and the client runtime: the package the pipeline stages (templates from
    /// <c>deploy/template</c>, <c>Deployment.config.json</c> from the generator, the source in <c>Files</c>) runs through
    /// <c>Install.cmd</c> in Windows PowerShell and ends with the expected exit code.
    /// </summary>
    public class WrapperChainTests
    {
        private static string RepositoryRoot()
        {
            var directory = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "IntunePackageBuilder.sln")))
            {
                directory = directory.Parent;
            }

            Assert.NotNull(directory);
            return directory.FullName;
        }

        private static PackageVersionConfig ExeConfiguration(string installerArguments)
        {
            var config = PackageVersionConfig.CreateDefault("chain-app", InstallerType.Exe);
            config.Identity.SoftwareName = "Chain App";
            config.Identity.Manufacturer = "Contoso Ltd.";
            config.Identity.TargetVersion = "1.0.0";
            config.Source.InstallerRelativePath = "setup.cmd";
            config.Install.Arguments = installerArguments;
            config.Uninstall.ExecutablePath = "%SystemRoot%\\System32\\cmd.exe";
            config.Uninstall.Arguments = "/c exit 0";
            config.Detection.Path = "%SystemRoot%\\System32\\kernel32.dll";
            config.Detection.MinimumVersion = "1.0";
            return config;
        }

        /// <summary>Builds with the real templates and a fake packaging tool that keeps a copy of the staged package folder.</summary>
        private static string StagePackage(PipelineFixture fixture, PackageVersionConfig configuration)
        {
            fixture.ReplaceSource(source => File.WriteAllText(Path.Combine(source, "setup.cmd"), "@echo off\r\nexit /b %1\r\n"));
            fixture.Configuration = configuration;
            var request = fixture.Request("en");
            request.RuntimeTemplateDirectory = Path.Combine(RepositoryRoot(), "deploy", "template");
            var kept = Path.Combine(fixture.Root, "kept-package");
            var runner = new FakeRunner { OnPack = pack => CopyDirectory(pack.SetupFolder, kept) };

            new BuildPipeline(runner).Run(request, null, CancellationToken.None);
            return kept;
        }

        private static ProcessResult RunInstallCmd(string package, string logDirectory, params string[] extraArguments)
        {
            var arguments = new System.Collections.Generic.List<string>(extraArguments) { "-LogDirectory", logDirectory };
            return ProcessRunner.Run(Path.Combine(package, "Install.cmd"), arguments, package, TimeSpan.FromMinutes(5));
        }

        [Theory]
        [InlineData("0", 0)]
        [InlineData("3010", 3010)]
        [InlineData("1618", 1618)]
        [InlineData("1641", 1641)]
        [InlineData("5", 5)]
        public void ThePackageOfThePipelineRunsThroughInstallCmdAndPassesTheInstallerResult(string installerExitCode, int expectedExitCode)
        {
            using (var fixture = new PipelineFixture())
            {
                var package = StagePackage(fixture, ExeConfiguration(installerExitCode));
                var logDirectory = Path.Combine(fixture.Root, "logs");

                var result = RunInstallCmd(package, logDirectory);

                Assert.False(result.TimedOut);
                Assert.Equal(expectedExitCode, result.ExitCode);
                Assert.True(File.Exists(Path.Combine(logDirectory, "Deployment.log")));
            }
        }

        [Fact]
        public void TheStagedPackageHasTheLayoutTheWrapperExpects()
        {
            using (var fixture = new PipelineFixture())
            {
                var package = StagePackage(fixture, ExeConfiguration("0"));

                Assert.True(File.Exists(Path.Combine(package, "Install.cmd")));
                Assert.True(File.Exists(Path.Combine(package, "Deploy-Wrapper.ps1")));
                Assert.True(File.Exists(Path.Combine(package, "DeployCore.psm1")));
                Assert.True(File.Exists(Path.Combine(package, "Deployment.config.json")));
                Assert.True(File.Exists(Path.Combine(package, "Files", "setup.cmd")));
            }
        }

        [Fact]
        public void TheUninstallCommandOfTheConfigurationReachesTheWrapper()
        {
            using (var fixture = new PipelineFixture())
            {
                var package = StagePackage(fixture, ExeConfiguration("0"));
                var logDirectory = Path.Combine(fixture.Root, "logs");

                // The detection file (kernel32) is still there after the uninstall command, so the verification must say so.
                var result = RunInstallCmd(package, logDirectory, "-DeploymentType", "Uninstall");

                Assert.Equal(60001, result.ExitCode);
                var log = File.ReadAllText(Path.Combine(logDirectory, "Deployment.log"));
                Assert.Contains("cmd.exe", log);
                Assert.Contains("still present", log);
            }
        }

        private static void CopyDirectory(string source, string target)
        {
            Directory.CreateDirectory(target);
            foreach (var file in Directory.GetFiles(source))
            {
                File.Copy(file, Path.Combine(target, Path.GetFileName(file)), true);
            }

            foreach (var directory in Directory.GetDirectories(source))
            {
                CopyDirectory(directory, Path.Combine(target, Path.GetFileName(directory)));
            }
        }
    }
}
