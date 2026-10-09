using System.Collections.Generic;
using System.IO;
using System.Linq;
using IntunePackageBuilder.Analysis.Exe;
using IntunePackageBuilder.Analysis.Msi;
using IntunePackageBuilder.Build.Workflow;
using IntunePackageBuilder.Core.Sources;
using IntunePackageBuilder.Core.Versions;
using Xunit;

namespace IntunePackageBuilder.Build.Tests
{
    public class SourceAnalyzerTests
    {
        private static MsiMetadata Msi(bool requiresSourceFolder = false, string[] cabinets = null, string allUsers = "1")
        {
            var properties = new Dictionary<string, string>
            {
                { "ProductName", "Contoso Reader" },
                { "Manufacturer", "Contoso Ltd." },
                { "ProductVersion", "4.2.1" },
                { "ProductCode", "{8F2C41A7-5B3E-4D19-A7C0-3E6D21B94F05}" }
            };
            if (allUsers != null)
            {
                properties["ALLUSERS"] = allUsers;
            }

            return new MsiMetadata(properties, cabinets ?? new string[0], requiresSourceFolder);
        }

        [Fact]
        public void AnMsiGivesTheFormItsValuesAndNeedsNoTechnicalInput()
        {
            var analysis = SourceAnalyzer.FromMsi(Msi());
            var config = analysis.CreateConfiguration();

            Assert.Equal(InstallerType.Msi, analysis.InstallerType);
            Assert.Equal("Contoso Reader", config.Identity.SoftwareName);
            Assert.Equal("Contoso Ltd.", config.Identity.Manufacturer);
            Assert.Equal("4.2.1", config.Identity.TargetVersion);
            Assert.Equal("{8F2C41A7-5B3E-4D19-A7C0-3E6D21B94F05}", config.Install.ProductCode);
            Assert.Equal("4.2.1", config.Detection.MinimumVersion);
            Assert.Equal(DetectionMethod.MsiProductCode, config.Detection.Method);
            Assert.Empty(analysis.Notes);
        }

        [Fact]
        public void AnMsiConfigurationIsCompleteOnceTheSourceIsNamed()
        {
            var config = SourceAnalyzer.FromMsi(Msi()).CreateConfiguration();
            config.ProjectId = "contoso-reader";
            config.Source.InstallerRelativePath = "setup.msi";

            Assert.Empty(ConfigurationValidator.Validate(config));
        }

        [Fact]
        public void AnMsiThatNeedsItsFolderOrSetsNoAllUsersCarriesHints()
        {
            var analysis = SourceAnalyzer.FromMsi(Msi(requiresSourceFolder: true, cabinets: new[] { "data1.cab" }, allUsers: null));

            Assert.Equal(
                new[] { AnalysisNote.MsiNeedsSourceFolder, AnalysisNote.MsiExternalCabinets, AnalysisNote.MsiPerUserDefault },
                analysis.Notes);
        }

        [Fact]
        public void AnExeGivesSuggestionsAndLeavesTheVendorValuesEmpty()
        {
            var metadata = new ExeMetadata("Fabrikam Editor", "Fabrikam", null, "setup.exe", "12.0.3.0", "12.0.3.0");

            var analysis = SourceAnalyzer.FromExe(metadata);
            var config = analysis.CreateConfiguration();

            Assert.Equal(InstallerType.Exe, analysis.InstallerType);
            Assert.Equal("Fabrikam Editor", config.Identity.SoftwareName);
            Assert.Equal("Fabrikam", config.Identity.Manufacturer);
            Assert.Equal(config.Identity.TargetVersion, config.Detection.MinimumVersion);
            Assert.True(string.IsNullOrEmpty(config.Install.Arguments));
            Assert.True(string.IsNullOrEmpty(config.Uninstall.ExecutablePath));
            Assert.True(string.IsNullOrEmpty(config.Detection.Path));
            Assert.Equal(new[] { AnalysisNote.ExeNeedsVendorInput }, analysis.Notes);

            config.ProjectId = "fabrikam-editor";
            config.Source.InstallerRelativePath = "setup.exe";
            var fields = ConfigurationValidator.Validate(config).Select(i => i.Field).ToList();
            Assert.Contains("install.arguments", fields);
            Assert.Contains("detection.path", fields);
        }

        [Fact]
        public void AnExeWithoutVersionInformationSaysSo()
        {
            var analysis = SourceAnalyzer.FromExe(new ExeMetadata(null, null, null, null, null, null));

            Assert.Contains(AnalysisNote.ExeNoVersionInfo, analysis.Notes);
            Assert.Null(analysis.SuggestedName);
        }

        [Fact]
        public void ARealExeIsReadWithoutRunningIt()
        {
            using (var directory = new TempDirectory())
            {
                var copy = directory.Combine("setup.exe");
                File.Copy(Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.System), "cmd.exe"), copy);

                var analysis = SourceAnalyzer.Analyze(new DroppedItem(DroppedKind.Exe, copy));

                Assert.Equal(InstallerType.Exe, analysis.InstallerType);
                Assert.Equal(ImportKind.SingleFile, analysis.ImportKind);
                Assert.Equal("setup.exe", analysis.InstallerRelativePath);
                Assert.False(string.IsNullOrWhiteSpace(analysis.SuggestedName));
                Assert.NotNull(analysis.Exe);
            }
        }

        [Fact]
        public void AFolderNeedsTheInstallerToBeNamed()
        {
            using (var directory = new TempDirectory())
            {
                Directory.CreateDirectory(directory.Combine("vendor", "bin"));
                File.Copy(Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.System), "cmd.exe"), directory.Combine("vendor", "bin", "setup.exe"));
                var item = new DroppedItem(DroppedKind.Folder, directory.Combine("vendor"));

                Assert.Equal(AnalysisProblem.InstallerNotSelected, Assert.Throws<AnalysisFailedException>(() => SourceAnalyzer.Analyze(item)).Problem);

                var analysis = SourceAnalyzer.Analyze(item, "bin\\setup.exe");
                Assert.Equal(ImportKind.Folder, analysis.ImportKind);
                Assert.Equal("bin/setup.exe", analysis.InstallerRelativePath);
            }
        }

        [Theory]
        [InlineData("..\\outside.exe")]
        [InlineData("C:\\Windows\\System32\\cmd.exe")]
        [InlineData("/etc/x.exe")]
        [InlineData("a//b.exe")]
        public void AnInstallerOutsideTheDroppedFolderIsRefused(string relative)
        {
            using (var directory = new TempDirectory())
            {
                var item = new DroppedItem(DroppedKind.Folder, directory.Path);

                Assert.Equal(AnalysisProblem.InstallerOutsideFolder, Assert.Throws<AnalysisFailedException>(() => SourceAnalyzer.Analyze(item, relative)).Problem);
            }
        }

        [Fact]
        public void FilesThatAreNotInstallersAreRefusedWithAClearReason()
        {
            using (var directory = new TempDirectory())
            {
                var fakeExe = directory.Combine("fake.exe");
                File.WriteAllText(fakeExe, "not an executable");
                var fakeMsi = directory.Combine("fake.msi");
                File.WriteAllText(fakeMsi, "not an installer database");
                var text = directory.Combine("readme.txt");
                File.WriteAllText(text, "x");

                Assert.Equal(AnalysisProblem.NotAnInstaller, Assert.Throws<AnalysisFailedException>(() => SourceAnalyzer.Analyze(new DroppedItem(DroppedKind.Exe, fakeExe))).Problem);
                Assert.Equal(AnalysisProblem.NotAnInstaller, Assert.Throws<AnalysisFailedException>(() => SourceAnalyzer.Analyze(new DroppedItem(DroppedKind.Msi, fakeMsi))).Problem);
                Assert.Equal(AnalysisProblem.UnsupportedFileType, Assert.Throws<AnalysisFailedException>(() => SourceAnalyzer.Analyze(new DroppedItem(DroppedKind.Exe, text))).Problem);
                Assert.Equal(AnalysisProblem.FileMissing, Assert.Throws<AnalysisFailedException>(() => SourceAnalyzer.Analyze(new DroppedItem(DroppedKind.Exe, directory.Combine("absent.exe")))).Problem);
            }
        }
    }
}
