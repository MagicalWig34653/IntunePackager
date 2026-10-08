using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using IntunePackageBuilder.Analysis.Msi;
using Microsoft.Win32;
using Xunit;

namespace IntunePackageBuilder.Analysis.Tests
{
    public sealed class MsiReaderTests : IDisposable
    {
        private const string ProductCode = "{8F2C41A7-5B3E-4D19-A7C0-3E6D21B94F05}";

        private readonly string _root = Path.Combine(Path.GetTempPath(), "ipb-msi-" + Guid.NewGuid().ToString("N"));

        public MsiReaderTests()
        {
            Directory.CreateDirectory(_root);
        }

        public void Dispose()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, true);
            }
        }

        private string MsiPath(string name = "setup.msi")
        {
            return Path.Combine(_root, name);
        }

        private static string Sha256(string path)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(path))
            {
                return BitConverter.ToString(sha.ComputeHash(stream));
            }
        }

        [Fact]
        public void Read_ReturnsTheIdentityValues()
        {
            var path = TestMsi.WithIdentity(ProductCode, "4.2.1")
                .Property("ProductName", "Contoso Reader")
                .Property("Manufacturer", "Contoso Ltd.")
                .Property("UpgradeCode", "{11111111-2222-3333-4444-555555555555}")
                .Property("ALLUSERS", "1")
                .Save(MsiPath());

            var metadata = MsiReader.Read(path);

            Assert.Equal("Contoso Reader", metadata.ProductName);
            Assert.Equal("Contoso Ltd.", metadata.Manufacturer);
            Assert.Equal("4.2.1", metadata.ProductVersion);
            Assert.Equal(ProductCode, metadata.ProductCode);
            Assert.Equal("{11111111-2222-3333-4444-555555555555}", metadata.UpgradeCode);
            Assert.Equal("1", metadata.AllUsers);
        }

        [Fact]
        public void Read_ReportsMissingOptionalValuesAsNull()
        {
            var path = TestMsi.WithIdentity(ProductCode, "1.0").Save(MsiPath());

            var metadata = MsiReader.Read(path);

            Assert.Null(metadata.ProductName);
            Assert.Null(metadata.Manufacturer);
            Assert.Null(metadata.UpgradeCode);
            Assert.Null(metadata.AllUsers);
        }

        [Fact]
        public void Read_KeepsSpecialCharactersIntact()
        {
            var name = "Quote ' \" back\\slash <b>&amp;</b> \u00e4\u00f6\u00fc\u00df \u20ac {braces}";
            var path = TestMsi.WithIdentity(ProductCode, "1.0").Property("ProductName", name).Save(MsiPath());

            Assert.Equal(name, MsiReader.Read(path).ProductName);
        }

        [Fact]
        public void Read_ReadsLongValues()
        {
            var longValue = new string('x', 5000);
            var path = TestMsi.WithIdentity(ProductCode, "1.0").Property("ARPCOMMENTS", longValue).Save(MsiPath());

            Assert.Equal(longValue, MsiReader.Read(path).Properties["ARPCOMMENTS"]);
        }

        [Fact]
        public void Read_ExposesAllProperties()
        {
            var path = TestMsi.WithIdentity(ProductCode, "1.0").Property("CustomThing", "value").Save(MsiPath());

            var properties = MsiReader.Read(path).Properties;

            Assert.Equal("value", properties["CustomThing"]);
            Assert.Equal(ProductCode, properties["ProductCode"]);
        }

        [Fact]
        public void Read_DoesNotChangeTheFile()
        {
            var path = TestMsi.WithIdentity(ProductCode, "1.0").Save(MsiPath());
            var before = Sha256(path);
            var writtenBefore = File.GetLastWriteTimeUtc(path);

            MsiReader.Read(path);
            MsiReader.Read(path);

            Assert.Equal(before, Sha256(path));
            Assert.Equal(writtenBefore, File.GetLastWriteTimeUtc(path));
        }

        [Fact]
        public void Read_NeverRunsCustomActionsOrInstalls()
        {
            var marker = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "ipb-test-marker.txt");
            File.Delete(marker);
            var path = TestMsi.WithIdentity(ProductCode, "1.0")
                .CustomActionThatWouldStartAProgram()
                .Save(MsiPath());

            var metadata = MsiReader.Read(path);

            Assert.Equal(ProductCode, metadata.ProductCode);
            Assert.False(File.Exists(marker), "A custom action must never run while reading.");
            using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\" + ProductCode))
            {
                Assert.Null(key);
            }
        }

        [Fact]
        public void Read_ReportsAMissingFile()
        {
            var ex = Assert.Throws<MsiReadException>(() => MsiReader.Read(MsiPath("missing.msi")));

            Assert.Equal(MsiReadProblem.FileNotFound, ex.Problem);
        }

        [Fact]
        public void Read_RejectsAFileThatIsNotAnInstallerDatabase()
        {
            var path = MsiPath("fake.msi");
            File.WriteAllText(path, "this is not an installer database");

            var ex = Assert.Throws<MsiReadException>(() => MsiReader.Read(path));

            Assert.Equal(MsiReadProblem.NotAnInstallerDatabase, ex.Problem);
        }

        [Fact]
        public void Read_RejectsAnEmptyFile()
        {
            var path = MsiPath("empty.msi");
            File.WriteAllBytes(path, new byte[0]);

            var ex = Assert.Throws<MsiReadException>(() => MsiReader.Read(path));

            Assert.Equal(MsiReadProblem.NotAnInstallerDatabase, ex.Problem);
        }

        [Fact]
        public void Read_RequiresAProductCode()
        {
            var path = TestMsi.WithIdentity(null, "1.0").Save(MsiPath());

            Assert.Equal(MsiReadProblem.MissingProductCode, Assert.Throws<MsiReadException>(() => MsiReader.Read(path)).Problem);
        }

        [Theory]
        [InlineData("8F2C41A7-5B3E-4D19-A7C0-3E6D21B94F05")]
        [InlineData("{not-a-guid}")]
        public void Read_RejectsAMalformedProductCode(string productCode)
        {
            var path = TestMsi.WithIdentity(productCode, "1.0").Save(MsiPath());

            Assert.Equal(MsiReadProblem.InvalidProductCode, Assert.Throws<MsiReadException>(() => MsiReader.Read(path)).Problem);
        }

        [Fact]
        public void Read_RequiresAProductVersion()
        {
            var path = TestMsi.WithIdentity(ProductCode, null).Save(MsiPath());

            Assert.Equal(MsiReadProblem.MissingProductVersion, Assert.Throws<MsiReadException>(() => MsiReader.Read(path)).Problem);
        }

        [Fact]
        public void Read_ReportsExternalCabinetsAsRequiredSourceFolder()
        {
            var path = TestMsi.WithIdentity(ProductCode, "1.0").Media("data1.cab", "#embedded.cab", "data2.cab").Files(3).Save(MsiPath());

            var metadata = MsiReader.Read(path);

            Assert.True(metadata.RequiresSourceFolder);
            Assert.Equal(new[] { "data1.cab", "data2.cab" }, metadata.ExternalCabinets.ToArray());
        }

        [Fact]
        public void Read_AnEmbeddedCabinetNeedsNoSourceFolder()
        {
            var path = TestMsi.WithIdentity(ProductCode, "1.0").Media("#product.cab").Files(2).Save(MsiPath());

            var metadata = MsiReader.Read(path);

            Assert.False(metadata.RequiresSourceFolder);
            Assert.Empty(metadata.ExternalCabinets);
        }

        [Fact]
        public void Read_UncompressedFilesNeedTheSourceFolder()
        {
            var path = TestMsi.WithIdentity(ProductCode, "1.0").Media(string.Empty).Files(2).Save(MsiPath());

            Assert.True(MsiReader.Read(path).RequiresSourceFolder);
        }

        [Fact]
        public void Read_AnMsiWithoutFilesNeedsNoSourceFolder()
        {
            var path = TestMsi.WithIdentity(ProductCode, "1.0").Save(MsiPath());

            Assert.False(MsiReader.Read(path).RequiresSourceFolder);
        }
    }
}
