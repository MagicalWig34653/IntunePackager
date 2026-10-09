using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using IntunePackageBuilder.Build.Packaging;
using IntunePackageBuilder.Build.Pipeline;
using IntunePackageBuilder.Core.Sources;
using IntunePackageBuilder.Core.Versions;

namespace IntunePackageBuilder.Build.Tests
{
    /// <summary>A fake packaging tool: records what it was given and writes a valid package, or fails on request.</summary>
    internal sealed class FakeRunner : IContentPrepRunner
    {
        public int Calls { get; private set; }

        public PackRequest LastRequest { get; private set; }

        /// <summary>Relative paths of the staged package content at the time of the call.</summary>
        public List<string> StagedFiles { get; private set; }

        public Exception FailWith { get; set; }

        public Action<PackRequest> OnPack { get; set; }

        public string Pack(PackRequest request)
        {
            Calls++;
            LastRequest = request;
            var root = request.SetupFolder.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            StagedFiles = Directory.EnumerateFiles(request.SetupFolder, "*", SearchOption.AllDirectories)
                .Select(f => f.Substring(root.Length).Replace('\\', '/'))
                .OrderBy(f => f, StringComparer.Ordinal)
                .ToList();
            if (OnPack != null)
            {
                OnPack(request);
            }

            if (FailWith != null)
            {
                throw FailWith;
            }

            Directory.CreateDirectory(request.OutputFolder);
            var output = Path.Combine(request.OutputFolder, Path.GetFileNameWithoutExtension(request.SetupFile) + ".intunewin");
            TestPackages.WriteIntunewin(output);
            return output;
        }
    }

    /// <summary>A stored version with a source and its manifest, a runtime template folder and a work folder.</summary>
    internal sealed class PipelineFixture : IDisposable
    {
        private readonly TempDirectory _directory = new TempDirectory();

        public PipelineFixture()
        {
            VersionDirectory = _directory.Combine("v", "1.0.0");
            SourceDirectory = Path.Combine(VersionDirectory, "source");
            TemplateDirectory = _directory.Combine("template");
            WorkRoot = _directory.Combine("work");
            Directory.CreateDirectory(SourceDirectory);
            Directory.CreateDirectory(TemplateDirectory);
            Directory.CreateDirectory(WorkRoot);
            File.WriteAllBytes(Path.Combine(SourceDirectory, "setup.msi"), new byte[] { 1, 2, 3, 4, 5 });
            File.WriteAllText(Path.Combine(SourceDirectory, "vendor.txt"), "vendor file");
            File.WriteAllText(Path.Combine(TemplateDirectory, "Install.cmd"), "@echo off\r\n");
            File.WriteAllText(Path.Combine(TemplateDirectory, "Deploy-Wrapper.ps1"), "# wrapper\r\n");
            Manifest = SourceManifestBuilder.Build(SourceDirectory);
            SourceManifestStore.WriteNew(Path.Combine(VersionDirectory, SourceManifest.FileName), Manifest);
            Configuration = MsiConfiguration();
        }

        public string Root
        {
            get { return _directory.Path; }
        }

        public string VersionDirectory { get; private set; }

        public string SourceDirectory { get; private set; }

        public string TemplateDirectory { get; private set; }

        public string WorkRoot { get; private set; }

        public SourceManifest Manifest { get; private set; }

        public PackageVersionConfig Configuration { get; set; }

        public string BuildsDirectory
        {
            get { return Path.Combine(VersionDirectory, "builds"); }
        }

        public static PackageVersionConfig MsiConfiguration()
        {
            var config = PackageVersionConfig.CreateDefault("contoso-reader", InstallerType.Msi);
            config.Identity.SoftwareName = "Contoso Reader";
            config.Identity.Manufacturer = "Contoso Ltd.";
            config.Identity.TargetVersion = "1.0.0";
            config.Source.InstallerRelativePath = "setup.msi";
            config.Install.ProductCode = "{8F2C41A7-5B3E-4D19-A7C0-3E6D21B94F05}";
            config.Detection.MinimumVersion = "1.0.0";
            return config;
        }

        public BuildRequest Request(string language = "en")
        {
            return new BuildRequest
            {
                VersionDirectory = VersionDirectory,
                Configuration = Configuration,
                Language = language,
                RuntimeTemplateDirectory = TemplateDirectory,
                WorkRoot = WorkRoot,
                ContentPrepToolPath = null,
                ToolTimeout = TimeSpan.FromMinutes(1)
            };
        }

        public BuildResult Build(FakeRunner runner, string language = "en")
        {
            return new BuildPipeline(runner).Run(Request(language), null, CancellationToken.None);
        }

        public IReadOnlyList<string> PublishedBuilds()
        {
            return Directory.Exists(BuildsDirectory)
                ? Directory.GetDirectories(BuildsDirectory).Select(Path.GetFileName).Where(n => !n.EndsWith(".partial", StringComparison.Ordinal)).OrderBy(n => n).ToList()
                : new List<string>();
        }

        public void Dispose()
        {
            _directory.Dispose();
        }
    }
}
