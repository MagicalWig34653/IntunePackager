using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using IntunePackageBuilder.Build.Packaging;
using IntunePackageBuilder.Build.Pipeline;
using IntunePackageBuilder.Build.Psadt;
using IntunePackageBuilder.Core.Builds;
using IntunePackageBuilder.Core.Versions;
using Xunit;

namespace IntunePackageBuilder.Build.Tests
{
    /// <summary>A toolkit ZIP as small as the checks allow, its pin file and the templates of the toolkit engine.</summary>
    internal sealed class ToolkitFixture : IDisposable
    {
        private readonly TempDirectory _directory = new TempDirectory();

        public ToolkitFixture(Action<ZipArchive> extra = null, bool pin = true)
        {
            ZipPath = _directory.Combine("PSAppDeployToolkit_Template_v4.zip");
            using (var stream = new FileStream(ZipPath, FileMode.Create, FileAccess.Write))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                foreach (var name in new[]
                {
                    "PSAppDeployToolkit/PSAppDeployToolkit.psd1", "PSAppDeployToolkit/PSAppDeployToolkit.psm1", "PSAppDeployToolkit/COPYING.Lesser",
                    "PSAppDeployToolkit/Config/config.psd1", "PSAppDeployToolkit/Strings/strings.psd1", "PSAppDeployToolkit/lib/PSADT.dll",
                    "PSAppDeployToolkit/Frontend/v4/Invoke-AppDeployToolkit.ps1", "PSAppDeployToolkit/lib/PSADT.pdb", "PSAppDeployToolkit/lib/de-DE/x.resources.dll",
                    "Invoke-AppDeployToolkit.ps1", "Config/config.psd1"
                })
                {
                    Write(archive, name, "content of " + name);
                }

                if (extra != null)
                {
                    extra(archive);
                }
            }

            Sha256 = ContentPrepTool.ComputeSha256(ZipPath);
            PinPath = _directory.Combine("psadt.json");
            File.WriteAllText(
                PinPath,
                "{\"schemaVersion\":1,\"versions\":[" + (pin ? "{\"version\":\"9.9.9\",\"asset\":\"PSAppDeployToolkit_Template_v4.zip\",\"sha256\":\"" + Sha256 + "\",\"commit\":\"" + new string('b', 40) + "\"}" : string.Empty) + "]}",
                new UTF8Encoding(false));
            TemplateDirectory = _directory.Combine("psadt-template");
            Directory.CreateDirectory(TemplateDirectory);
            File.WriteAllText(Path.Combine(TemplateDirectory, "Install.cmd"), "@echo off\r\nrem toolkit\r\n");
            File.WriteAllText(Path.Combine(TemplateDirectory, "Invoke-AppDeployToolkit.ps1"), "# toolkit entry\r\n");
        }

        public string ZipPath { get; private set; }

        public string PinPath { get; private set; }

        public string Sha256 { get; private set; }

        public string TemplateDirectory { get; private set; }

        public string Root
        {
            get { return _directory.Path; }
        }

        public PsadtSupply Supply(bool allowUnknown = false)
        {
            return new PsadtSupply { TemplateDirectory = TemplateDirectory, PackagePath = ZipPath, PinPath = PinPath, AllowUnknown = allowUnknown };
        }

        public static void Write(ZipArchive archive, string name, string text)
        {
            var entry = archive.CreateEntry(name);
            using (var stream = entry.Open())
            {
                var bytes = Encoding.UTF8.GetBytes(text);
                stream.Write(bytes, 0, bytes.Length);
            }
        }

        public void Dispose()
        {
            _directory.Dispose();
        }
    }

    public class PsadtPackageTests
    {
        [Fact]
        public void APinnedZipIsRecognisedWithItsVersionAndSize()
        {
            using (var toolkit = new ToolkitFixture())
            {
                var info = PsadtPackage.Inspect(toolkit.ZipPath, toolkit.PinPath, false);

                Assert.Equal("9.9.9", info.Version);
                Assert.Equal(toolkit.Sha256, info.Sha256);
                Assert.True(info.UncompressedBytes > 0);
            }
        }

        [Fact]
        public void AnUnknownZipNeedsAnExplicitConfirmation()
        {
            using (var toolkit = new ToolkitFixture(pin: false))
            {
                var refused = Assert.Throws<PsadtException>(() => PsadtPackage.Inspect(toolkit.ZipPath, toolkit.PinPath, false));
                Assert.Equal(PsadtProblem.PackageNotRecognized, refused.Problem);
                Assert.Contains(toolkit.Sha256, refused.Detail);

                var confirmed = PsadtPackage.Inspect(toolkit.ZipPath, toolkit.PinPath, true);
                Assert.Null(confirmed.Version);
                Assert.Equal(toolkit.Sha256, confirmed.Sha256);
            }
        }

        [Fact]
        public void AMissingFileAndAMissingPinFileAreReported()
        {
            using (var toolkit = new ToolkitFixture())
            {
                Assert.Equal(PsadtProblem.PackageMissing, Assert.Throws<PsadtException>(() => PsadtPackage.Inspect(Path.Combine(toolkit.Root, "none.zip"), toolkit.PinPath, false)).Problem);
                Assert.Equal(PsadtProblem.PackageMissing, Assert.Throws<PsadtException>(() => PsadtPackage.Inspect(null, toolkit.PinPath, false)).Problem);
                Assert.Equal(PsadtProblem.PinMissing, Assert.Throws<PsadtException>(() => PsadtPackage.Inspect(toolkit.ZipPath, Path.Combine(toolkit.Root, "none.json"), false)).Problem);
            }
        }

        [Fact]
        public void ABrokenPinFileIsReportedAsMissing()
        {
            using (var toolkit = new ToolkitFixture())
            {
                File.WriteAllText(toolkit.PinPath, "{ not json");
                Assert.Equal(PsadtProblem.PinMissing, Assert.Throws<PsadtException>(() => PsadtPackage.Inspect(toolkit.ZipPath, toolkit.PinPath, false)).Problem);
            }
        }

        [Fact]
        public void ANonZipFileIsUnreadable()
        {
            using (var toolkit = new ToolkitFixture(pin: false))
            {
                var text = Path.Combine(toolkit.Root, "text.zip");
                File.WriteAllText(text, "this is not a zip");

                var exception = Assert.Throws<PsadtException>(() => PsadtPackage.Inspect(text, toolkit.PinPath, true));

                Assert.Equal(PsadtProblem.PackageUnreadable, exception.Problem);
            }
        }

        [Fact]
        public void AZipWithoutTheModuleFilesIsInvalid()
        {
            using (var toolkit = new ToolkitFixture(pin: false))
            {
                var empty = Path.Combine(toolkit.Root, "empty.zip");
                using (var stream = new FileStream(empty, FileMode.Create))
                using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
                {
                    ToolkitFixture.Write(archive, "PSAppDeployToolkit/PSAppDeployToolkit.psd1", "x");
                }

                var exception = Assert.Throws<PsadtException>(() => PsadtPackage.Inspect(empty, toolkit.PinPath, true));

                Assert.Equal(PsadtProblem.PackageInvalid, exception.Problem);
            }
        }

        [Theory]
        [InlineData("PSAppDeployToolkit/../evil.txt")]
        [InlineData("PSAppDeployToolkit/..\\evil.txt")]
        [InlineData("PSAppDeployToolkit/C:/evil.txt")]
        public void APathThatLeavesTheModuleFolderIsRefused(string name)
        {
            using (var toolkit = new ToolkitFixture(archive => ToolkitFixture.Write(archive, name, "evil"), pin: false))
            {
                var exception = Assert.Throws<PsadtException>(() => PsadtPackage.Inspect(toolkit.ZipPath, toolkit.PinPath, true));

                Assert.Equal(PsadtProblem.PackageUnsafe, exception.Problem);
            }
        }

        [Fact]
        public void OnlyTheModuleIsCopiedWithoutTheOldFrontEndAndWithoutDebugSymbols()
        {
            using (var toolkit = new ToolkitFixture())
            using (var target = new TempDirectory())
            {
                PsadtPackage.ExtractModule(toolkit.ZipPath, target.Path, null);

                var files = Directory.EnumerateFiles(target.Path, "*", SearchOption.AllDirectories)
                    .Select(f => f.Substring(target.Path.Length + 1).Replace('\\', '/'))
                    .OrderBy(f => f, StringComparer.Ordinal)
                    .ToList();
                Assert.Equal(
                    new[]
                    {
                        "PSAppDeployToolkit/COPYING.Lesser",
                        "PSAppDeployToolkit/Config/config.psd1",
                        "PSAppDeployToolkit/PSAppDeployToolkit.psd1",
                        "PSAppDeployToolkit/PSAppDeployToolkit.psm1",
                        "PSAppDeployToolkit/Strings/strings.psd1",
                        "PSAppDeployToolkit/lib/PSADT.dll",
                        "PSAppDeployToolkit/lib/de-DE/x.resources.dll"
                    }.OrderBy(f => f, StringComparer.Ordinal).ToArray(),
                    files.ToArray());
                Assert.Equal("content of PSAppDeployToolkit/lib/PSADT.dll", File.ReadAllText(Path.Combine(target.Path, "PSAppDeployToolkit", "lib", "PSADT.dll")));
            }
        }

        [Fact]
        public void AnExtractionNeverOverwritesAndHonoursThePathCheck()
        {
            using (var toolkit = new ToolkitFixture())
            using (var target = new TempDirectory())
            {
                PsadtPackage.ExtractModule(toolkit.ZipPath, target.Path, null);
                Assert.Throws<IOException>(() => PsadtPackage.ExtractModule(toolkit.ZipPath, target.Path, null));

                using (var other = new TempDirectory())
                {
                    Assert.Throws<PathTooLongException>(() => PsadtPackage.ExtractModule(toolkit.ZipPath, other.Path, path => { throw new PathTooLongException(path); }));
                }
            }
        }
    }

    public class PsadtAssetsTests
    {
        private static readonly byte[] PngHeader = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0 };
        private static readonly byte[] JpegHeader = { 0xFF, 0xD8, 0xFF, 0xE0, 0, 0 };

        [Fact]
        public void AnImageIsCopiedUnderANameThatDependsOnlyOnItsKindAndContent()
        {
            using (var temp = new TempDirectory())
            {
                var source = temp.Combine("My Company Logo (final).dat");
                File.WriteAllBytes(source, PngHeader);

                var name = PsadtAssets.Import(temp.Path, PsadtImageKind.Logo, source);

                Assert.Equal("logo.png", name);
                Assert.True(ConfigurationValidatorAccepts(name));
                Assert.Equal(PngHeader, File.ReadAllBytes(Path.Combine(temp.Path, "psadt-assets", name)));
                Assert.Equal("logo-dark.png", PsadtAssets.Import(temp.Path, PsadtImageKind.LogoDark, source));
                Assert.Equal("banner.png", PsadtAssets.Import(temp.Path, PsadtImageKind.Banner, source));
            }
        }

        [Fact]
        public void ReplacingAnImageWithAnotherFormatLeavesNoStaleFile()
        {
            using (var temp = new TempDirectory())
            {
                var png = temp.Combine("a.bin");
                var jpg = temp.Combine("b.bin");
                File.WriteAllBytes(png, PngHeader);
                File.WriteAllBytes(jpg, JpegHeader);

                PsadtAssets.Import(temp.Path, PsadtImageKind.Banner, png);
                var name = PsadtAssets.Import(temp.Path, PsadtImageKind.Banner, jpg);

                Assert.Equal("banner.jpg", name);
                Assert.Equal(new[] { "banner.jpg" }, Directory.GetFiles(Path.Combine(temp.Path, "psadt-assets")).Select(Path.GetFileName).ToArray());
            }
        }

        [Fact]
        public void AFileThatIsNotAnImageOrIsTooLargeIsRefused()
        {
            using (var temp = new TempDirectory())
            {
                var text = temp.Combine("x.png");
                File.WriteAllText(text, "this is text with a png extension");
                var empty = temp.Combine("empty.png");
                File.WriteAllBytes(empty, new byte[0]);
                var large = temp.Combine("large.png");
                var bytes = new byte[PsadtAssets.MaxBytes + 1];
                Array.Copy(PngHeader, bytes, PngHeader.Length);
                File.WriteAllBytes(large, bytes);

                foreach (var path in new[] { text, empty, large, temp.Combine("missing.png"), null })
                {
                    var exception = Assert.Throws<PsadtException>(() => PsadtAssets.Import(temp.Path, PsadtImageKind.Logo, path));
                    Assert.Equal(PsadtProblem.ImageInvalid, exception.Problem);
                }

                Assert.False(Directory.Exists(Path.Combine(temp.Path, "psadt-assets")) && Directory.GetFiles(Path.Combine(temp.Path, "psadt-assets")).Length > 0);
            }
        }

        [Fact]
        public void ANewVersionKeepsTheBrandingOfItsTemplateWithoutOverwritingAnything()
        {
            using (var temp = new TempDirectory())
            {
                var from = temp.Combine("v1");
                var to = temp.Combine("v2");
                var source = temp.Combine("l.bin");
                File.WriteAllBytes(source, PngHeader);
                PsadtAssets.Import(from, PsadtImageKind.Logo, source);
                PsadtAssets.Import(to, PsadtImageKind.Logo, source);
                File.WriteAllBytes(Path.Combine(PsadtAssets.AssetsDirectory(to), "logo.png"), JpegHeader);
                PsadtAssets.Import(from, PsadtImageKind.Banner, source);

                PsadtAssets.CopyAll(from, to);

                Assert.Equal(JpegHeader, File.ReadAllBytes(Path.Combine(PsadtAssets.AssetsDirectory(to), "logo.png")));
                Assert.True(File.Exists(Path.Combine(PsadtAssets.AssetsDirectory(to), "banner.png")));
                PsadtAssets.CopyAll(temp.Combine("nothing"), to);
            }
        }

        [Fact]
        public void MissingImagesAreFoundByName()
        {
            using (var temp = new TempDirectory())
            {
                var options = new PsadtSection { LogoFile = "logo.png", BannerFile = "banner.png", LogoDarkFile = "logo.png" };
                var source = temp.Combine("l.bin");
                File.WriteAllBytes(source, PngHeader);
                PsadtAssets.Import(temp.Path, PsadtImageKind.Logo, source);

                Assert.Equal(new[] { "banner.png" }, PsadtAssets.Missing(temp.Path, options).ToArray());
                Assert.Equal(new[] { "logo.png", "banner.png" }, PsadtAssets.Referenced(options).ToArray());
            }
        }

        private static bool ConfigurationValidatorAccepts(string name)
        {
            return ConfigurationValidator.IsPsadtAssetFileName(name);
        }
    }

    public class ToolkitBuildTests
    {
        private static BuildRequest Request(PipelineFixture fixture, ToolkitFixture toolkit, bool allowUnknown = false)
        {
            fixture.Configuration.Deployment.Engine = DeploymentEngine.Psadt;
            File.WriteAllText(Path.Combine(fixture.TemplateDirectory, "DeployCore.psm1"), "# core\r\n");
            File.WriteAllText(Path.Combine(fixture.TemplateDirectory, "Messages.en.psd1"), "@{}\r\n");
            var request = fixture.Request();
            request.Psadt = toolkit.Supply(allowUnknown);
            return request;
        }

        private static BuildResult Run(BuildRequest request, FakeRunner runner)
        {
            return new BuildPipeline(runner).Run(request, null, System.Threading.CancellationToken.None);
        }

        [Fact]
        public void ThePackageHasTheToolkitEngineInsteadOfTheWrapperAndTheModuleAndTheOverlayFiles()
        {
            using (var fixture = new PipelineFixture())
            using (var toolkit = new ToolkitFixture())
            {
                var runner = new FakeRunner();

                Run(Request(fixture, toolkit), runner);

                Assert.Equal(
                    new[]
                    {
                        "Config/config.psd1",
                        "DeployCore.psm1",
                        "Deployment.config.json",
                        "Files/setup.msi",
                        "Files/vendor.txt",
                        "Install.cmd",
                        "Invoke-AppDeployToolkit.ps1",
                        "Messages.en.psd1",
                        "PSAppDeployToolkit/COPYING.Lesser",
                        "PSAppDeployToolkit/Config/config.psd1",
                        "PSAppDeployToolkit/PSAppDeployToolkit.psd1",
                        "PSAppDeployToolkit/PSAppDeployToolkit.psm1",
                        "PSAppDeployToolkit/Strings/strings.psd1",
                        "PSAppDeployToolkit/lib/PSADT.dll",
                        "PSAppDeployToolkit/lib/de-DE/x.resources.dll",
                        "Strings/de/strings.psd1",
                        "Strings/strings.psd1"
                    },
                    runner.StagedFiles.ToArray());
                Assert.DoesNotContain("Deploy-Wrapper.ps1", runner.StagedFiles);
                Assert.Equal("Install.cmd", runner.LastRequest.SetupFile);
            }
        }

        [Fact]
        public void TheEntryPointComesFromTheToolkitTemplateNotFromTheNativeOne()
        {
            using (var fixture = new PipelineFixture())
            using (var toolkit = new ToolkitFixture())
            {
                string installCmd = null;
                var runner = new FakeRunner { OnPack = r => installCmd = File.ReadAllText(Path.Combine(r.SetupFolder, "Install.cmd")) };

                Run(Request(fixture, toolkit), runner);

                Assert.Contains("rem toolkit", installCmd);
            }
        }

        [Fact]
        public void ImagesAreCopiedIntoTheAssetsFolderAndTheOverlayPointsAtThem()
        {
            using (var fixture = new PipelineFixture())
            using (var toolkit = new ToolkitFixture())
            {
                var logo = Path.Combine(toolkit.Root, "l.bin");
                File.WriteAllBytes(logo, new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1 });
                fixture.Configuration.Deployment.Psadt.LogoFile = PsadtAssets.Import(fixture.VersionDirectory, PsadtImageKind.Logo, logo);
                string overlay = null;
                var runner = new FakeRunner { OnPack = r => overlay = File.ReadAllText(Path.Combine(r.SetupFolder, "Config", "config.psd1")) };

                var result = Run(Request(fixture, toolkit), runner);

                Assert.Contains("Assets/logo.png", runner.StagedFiles);
                Assert.Contains("Logo = '..\\Assets\\logo.png'", overlay);
                var manifest = BuildManifestStore.Read(Path.Combine(result.BuildDirectory, BuildManifest.FileName));
                Assert.Equal("9.9.9", manifest.ToolkitVersion);
                Assert.Equal(toolkit.Sha256, manifest.ToolkitSha256);
                Assert.Equal(new[] { "psadt-assets/logo.png" }, manifest.ToolkitAssets.Select(a => a.Path).ToArray());
                Assert.Equal(ContentPrepTool.ComputeSha256(Path.Combine(fixture.VersionDirectory, "psadt-assets", "logo.png")), manifest.ToolkitAssets[0].Sha256);
            }
        }

        [Fact]
        public void ABuildWithTheNativeWrapperRecordsNoToolkit()
        {
            using (var fixture = new PipelineFixture())
            {
                var result = fixture.Build(new FakeRunner());
                var manifest = BuildManifestStore.Read(Path.Combine(result.BuildDirectory, BuildManifest.FileName));

                Assert.Null(manifest.ToolkitVersion);
                Assert.Null(manifest.ToolkitSha256);
                Assert.Null(manifest.ToolkitAssets);
            }
        }

        [Fact]
        public void TheWrapperConfigurationOfTheBuildNamesTheEngine()
        {
            using (var fixture = new PipelineFixture())
            using (var toolkit = new ToolkitFixture())
            {
                string json = null;
                var runner = new FakeRunner { OnPack = r => json = File.ReadAllText(Path.Combine(r.SetupFolder, "Deployment.config.json")) };

                Run(Request(fixture, toolkit), runner);

                Assert.Contains("\"engine\": \"Psadt\"", json);
                Assert.Contains("\"psadt\"", json);
            }
        }

        [Fact]
        public void WithoutAToolkitZipTheBuildStopsBeforeAnythingIsStaged()
        {
            using (var fixture = new PipelineFixture())
            using (var toolkit = new ToolkitFixture())
            {
                var request = Request(fixture, toolkit);
                request.Psadt = null;
                var runner = new FakeRunner();

                var failure = Assert.Throws<BuildFailedException>(() => Run(request, runner));

                Assert.Equal(BuildProblem.ToolkitMissing, failure.Problem);
                Assert.Equal(0, runner.Calls);
                Assert.Empty(fixture.PublishedBuilds());
            }
        }

        [Fact]
        public void AnUnpinnedZipStopsTheBuildUnlessConfirmed()
        {
            using (var fixture = new PipelineFixture())
            using (var toolkit = new ToolkitFixture(pin: false))
            {
                var failure = Assert.Throws<BuildFailedException>(() => Run(Request(fixture, toolkit), new FakeRunner()));
                Assert.Equal(BuildProblem.ToolkitNotRecognized, failure.Problem);

                var result = Run(Request(fixture, toolkit, allowUnknown: true), new FakeRunner());
                var manifest = BuildManifestStore.Read(Path.Combine(result.BuildDirectory, BuildManifest.FileName));
                Assert.Null(manifest.ToolkitVersion);
                Assert.Equal(toolkit.Sha256, manifest.ToolkitSha256);
            }
        }

        [Fact]
        public void ABrokenZipAndAMissingImageStopTheBuild()
        {
            using (var fixture = new PipelineFixture())
            using (var toolkit = new ToolkitFixture(pin: false))
            {
                var request = Request(fixture, toolkit, allowUnknown: true);
                File.WriteAllText(toolkit.ZipPath, "not a zip");
                Assert.Equal(BuildProblem.ToolkitInvalid, Assert.Throws<BuildFailedException>(() => Run(request, new FakeRunner())).Problem);
            }

            using (var fixture = new PipelineFixture())
            using (var toolkit = new ToolkitFixture())
            {
                fixture.Configuration.Deployment.Psadt.BannerFile = "banner.png";
                var failure = Assert.Throws<BuildFailedException>(() => Run(Request(fixture, toolkit), new FakeRunner()));
                Assert.Equal(BuildProblem.ToolkitAssetsMissing, failure.Problem);
                Assert.Contains("banner.png", failure.Detail);
            }
        }

        [Fact]
        public void AMissingToolkitTemplateIsReportedAsAMissingTemplate()
        {
            using (var fixture = new PipelineFixture())
            using (var toolkit = new ToolkitFixture())
            {
                File.Delete(Path.Combine(toolkit.TemplateDirectory, "Invoke-AppDeployToolkit.ps1"));

                var failure = Assert.Throws<BuildFailedException>(() => Run(Request(fixture, toolkit), new FakeRunner()));

                Assert.Equal(BuildProblem.TemplateMissing, failure.Problem);
            }
        }
    }
}
