using System;
using System.IO;
using IntunePackageBuilder.Analysis.Exe;
using Xunit;

namespace IntunePackageBuilder.Analysis.Tests
{
    public sealed class ExeReaderTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "ipb-exe-" + Guid.NewGuid().ToString("N"));

        public ExeReaderTests()
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

        private static string SystemExe(string name)
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), name);
        }

        [Fact]
        public void Read_ReturnsTheVersionInformationOfARealExecutable()
        {
            var metadata = ExeReader.Read(SystemExe("cmd.exe"));

            Assert.True(metadata.HasVersionInfo);
            Assert.Contains("Microsoft", metadata.CompanyName);
            Assert.NotNull(metadata.FileVersion);
            Assert.NotNull(metadata.SuggestedSoftwareName);
            Assert.Equal(metadata.CompanyName, metadata.SuggestedManufacturer);
            Assert.NotNull(metadata.SuggestedVersion);
        }

        [Fact]
        public void Read_AnExecutableWithoutVersionInformationHasNoSuggestions()
        {
            var path = Path.Combine(_root, "plain.exe");
            var bytes = new byte[128];
            bytes[0] = (byte)'M';
            bytes[1] = (byte)'Z';
            File.WriteAllBytes(path, bytes);

            var metadata = ExeReader.Read(path);

            Assert.False(metadata.HasVersionInfo);
            Assert.Null(metadata.SuggestedSoftwareName);
            Assert.Null(metadata.SuggestedManufacturer);
            Assert.Null(metadata.SuggestedVersion);
        }

        [Fact]
        public void Read_ReportsAMissingFile()
        {
            var ex = Assert.Throws<ExeReadException>(() => ExeReader.Read(Path.Combine(_root, "missing.exe")));

            Assert.Equal(ExeReadProblem.FileNotFound, ex.Problem);
        }

        [Fact]
        public void Read_RejectsATextFileNamedExe()
        {
            var path = Path.Combine(_root, "fake.exe");
            File.WriteAllText(path, new string('x', 200));

            Assert.Equal(ExeReadProblem.NotAnExecutable, Assert.Throws<ExeReadException>(() => ExeReader.Read(path)).Problem);
        }

        [Fact]
        public void Read_RejectsAFileThatIsTooShort()
        {
            var path = Path.Combine(_root, "short.exe");
            File.WriteAllBytes(path, new byte[] { (byte)'M', (byte)'Z' });

            Assert.Equal(ExeReadProblem.NotAnExecutable, Assert.Throws<ExeReadException>(() => ExeReader.Read(path)).Problem);
        }

        [Theory]
        [InlineData("12.0.0.0", null, "12.0")]
        [InlineData("1.2.3.0", null, "1.2.3")]
        [InlineData("4.10.0.7", null, "4.10.0.7")]
        [InlineData("1.0.0.0", null, "1.0")]
        [InlineData(null, "2.5.1", "2.5.1")]
        [InlineData(null, "2.5 beta", null)]
        [InlineData(null, null, null)]
        public void SuggestedVersion_ShortensTrailingZerosAndNeedsANumericVersion(string fileVersion, string productVersion, string expected)
        {
            var metadata = new ExeMetadata(null, null, null, null, productVersion, fileVersion);

            Assert.Equal(expected, metadata.SuggestedVersion);
        }

        [Fact]
        public void SuggestedSoftwareName_FallsBackToTheDescription()
        {
            Assert.Equal("Product", new ExeMetadata("Product", null, "Description", null, null, null).SuggestedSoftwareName);
            Assert.Equal("Description", new ExeMetadata(null, null, "Description", null, null, null).SuggestedSoftwareName);
        }
    }
}
