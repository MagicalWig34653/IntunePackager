using System.IO;
using System.Linq;
using System.Text;
using IntunePackageBuilder.Generation.Intune;
using IntunePackageBuilder.Generation.Scripts;
using Xunit;

namespace IntunePackageBuilder.Generation.Tests
{
    public class DetectionScriptGeneratorTests
    {
        private static string MsiScript()
        {
            return DetectionScriptGenerator.Generate(IntuneSettingsBuilder.From(Samples.Snapshot(Samples.Msi())));
        }

        private static string ExeScript()
        {
            return DetectionScriptGenerator.Generate(IntuneSettingsBuilder.From(Samples.Snapshot(Samples.Exe())));
        }

        [Fact]
        public void TheMsiScriptChecksTheProductCodeInBothRegistryViewsAgainstTheMinimumVersion()
        {
            var script = MsiScript();

            Assert.Contains("$productCode = '" + Samples.ProductCode + "'", script);
            Assert.Contains("$minimumVersion = '4.2.1'", script);
            Assert.Contains("Registry64", script);
            Assert.Contains("Registry32", script);
            Assert.Contains("DisplayVersion", script);
            Assert.Contains("Test-VersionAtLeast", script);
        }

        [Fact]
        public void TheExeScriptChecksTheFileVersionOfTheConfiguredFile()
        {
            var script = ExeScript();

            Assert.Contains("$filePath = 'C:\\Program Files\\Fabrikam\\editor.exe'", script);
            Assert.Contains("$minimumVersion = '12.0.3'", script);
            Assert.Contains("GetVersionInfo", script);
            Assert.DoesNotContain("Registry64", script);
        }

        [Fact]
        public void TheScriptFollowsTheIntuneContract()
        {
            foreach (var script in new[] { MsiScript(), ExeScript() })
            {
                Assert.Contains("Write-Output $state", script);
                Assert.Contains("exit 0", script);
                Assert.Equal(1, DetectionScriptGenerator.NotDetectedExitCode);
                Assert.EndsWith("exit 1\r\n", script);
                Assert.Equal(1, CountOccurrences(script, "Write-Output"));
            }
        }

        [Fact]
        public void TheScriptIsStandalone()
        {
            foreach (var script in new[] { MsiScript(), ExeScript() })
            {
                Assert.DoesNotContain("PSScriptRoot", script);
                Assert.DoesNotContain("IntuneManagementExtension", script);
                Assert.DoesNotContain("Get-Location", script);
                Assert.DoesNotContain("Set-Content", script);
                Assert.DoesNotContain("New-Item", script);
                Assert.DoesNotContain("Out-File", script);
            }
        }

        [Fact]
        public void TheScriptUsesNoPowerShellSevenSyntaxAndWindowsLineEndings()
        {
            foreach (var script in new[] { MsiScript(), ExeScript() })
            {
                Assert.DoesNotContain("??", script);
                Assert.DoesNotContain("&&", script);
                Assert.DoesNotContain("||", script);
                Assert.DoesNotContain("-Parallel", script);
                Assert.DoesNotContain("\n", script.Replace("\r\n", string.Empty));
            }
        }

        [Fact]
        public void TheScriptRecordsTheBuildId()
        {
            Assert.Contains("# Build: " + Samples.BuildIdValue + "\r\n", MsiScript());
        }

        [Fact]
        public void AWrittenScriptStartsWithTheUtf8Bom()
        {
            var path = Path.Combine(Path.GetTempPath(), "ipb-detect-" + System.Guid.NewGuid().ToString("N") + ".ps1");
            try
            {
                DetectionScriptGenerator.WriteTo(path, ExeScript());

                var bytes = File.ReadAllBytes(path);
                Assert.True(bytes.Take(3).SequenceEqual(new byte[] { 0xEF, 0xBB, 0xBF }));
                Assert.Equal(ExeScript(), new UTF8Encoding(false).GetString(bytes, 3, bytes.Length - 3));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Theory]
        [InlineData("C:\\Apps\\it's here\\a.exe", "C:\\Apps\\it''s here\\a.exe")]
        [InlineData("C:\\Apps\\\u2018x\\a.exe", "C:\\Apps\\\u2018\u2018x\\a.exe")]
        [InlineData("C:\\Apps\\\u2019x\\a.exe", "C:\\Apps\\\u2019\u2019x\\a.exe")]
        [InlineData("C:\\Apps\\\u201Ax\\a.exe", "C:\\Apps\\\u201A\u201Ax\\a.exe")]
        [InlineData("C:\\Apps\\\u201Bx\\a.exe", "C:\\Apps\\\u201B\u201Bx\\a.exe")]
        public void EveryPowerShellQuoteCharacterInAPathIsDoubled(string path, string expectedInnerText)
        {
            var script = DetectionScriptGenerator.GenerateForFile(Samples.BuildIdValue, path, "1.0");

            Assert.Contains("$filePath = '" + expectedInnerText + "'\r\n", script);
        }

        [Fact]
        public void APathWithPowerShellSyntaxStaysInsideTheLiteral()
        {
            var path = "C:\\x'; Write-Output pwned; '\\a.exe";

            var script = DetectionScriptGenerator.GenerateForFile(Samples.BuildIdValue, path, "1.0");

            Assert.Contains("$filePath = 'C:\\x''; Write-Output pwned; ''\\a.exe'\r\n", script);
            Assert.DoesNotContain("\r\nWrite-Output pwned", script);
            Assert.DoesNotContain("\r\n; Write-Output", script);
        }

        [Theory]
        [InlineData("C:\\a\nWrite-Output x.exe")]
        [InlineData("C:\\a\rb.exe")]
        [InlineData("C:\\a\u2028b.exe")]
        [InlineData("C:\\a\u0000b.exe")]
        [InlineData("C:\\a\u202Eb.exe")]
        public void ControlAndLineSeparatorCharactersAreRejected(string path)
        {
            Assert.Throws<UnsafeScriptValueException>(() => DetectionScriptGenerator.GenerateForFile(Samples.BuildIdValue, path, "1.0"));
        }

        [Theory]
        [InlineData("")]
        [InlineData("8F2C41A7-5B3E-4D19-A7C0-3E6D21B94F05")]
        [InlineData("{8F2C41A7-5B3E-4D19-A7C0-3E6D21B94F0}")]
        [InlineData("{8F2C41A7-5B3E-4D19-A7C0-3E6D21B94F05}'; exit 0; '")]
        public void AnInvalidProductCodeIsRejected(string productCode)
        {
            Assert.Throws<UnsafeScriptValueException>(() => DetectionScriptGenerator.GenerateForMsi(Samples.BuildIdValue, productCode, "1.0"));
        }

        [Theory]
        [InlineData("")]
        [InlineData("1.0.0.0.1")]
        [InlineData("1.0'; exit 0; '")]
        [InlineData("1.x")]
        public void AnInvalidMinimumVersionIsRejected(string version)
        {
            Assert.Throws<UnsafeScriptValueException>(() => DetectionScriptGenerator.GenerateForMsi(Samples.BuildIdValue, Samples.ProductCode, version));
            Assert.Throws<UnsafeScriptValueException>(() => DetectionScriptGenerator.GenerateForFile(Samples.BuildIdValue, "C:\\a.exe", version));
        }

        [Fact]
        public void AnEmptyPathIsRejected()
        {
            Assert.Throws<UnsafeScriptValueException>(() => DetectionScriptGenerator.GenerateForFile(Samples.BuildIdValue, " ", "1.0"));
        }

        [Fact]
        public void ABuildIdWithLineBreaksCannotInjectCodeThroughTheComment()
        {
            var script = DetectionScriptGenerator.GenerateForFile("x\nWrite-Output pwned", "C:\\a.exe", "1.0");

            Assert.DoesNotContain("\r\nWrite-Output pwned", script);
            Assert.Contains("# Build: x?Write-Output pwned\r\n", script);
        }

        private static int CountOccurrences(string text, string value)
        {
            var count = 0;
            var index = 0;
            while ((index = text.IndexOf(value, index, System.StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += value.Length;
            }

            return count;
        }
    }
}
