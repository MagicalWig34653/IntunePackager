using System;
using System.IO;
using System.Linq;
using System.Text;
using IntunePackageBuilder.Core.Storage;
using IntunePackageBuilder.Core.Versions;
using Xunit;

namespace IntunePackageBuilder.Core.Tests
{
    public sealed class RestartCodeAndEncodingTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "ipb-encoding-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, true);
            }
        }

        private static PackageVersionConfig ValidMsi()
        {
            var config = PackageVersionConfig.CreateDefault("contoso-reader", InstallerType.Msi);
            config.Identity.SoftwareName = "Contoso Reader";
            config.Identity.Manufacturer = "Contoso Ltd.";
            config.Identity.TargetVersion = "4.2.1";
            config.Source.InstallerRelativePath = "setup.msi";
            config.Install.ProductCode = "{8F2C41A7-5B3E-4D19-A7C0-3E6D21B94F05}";
            config.Detection.MinimumVersion = "4.2.1";
            return config;
        }

        [Theory]
        [InlineData("success", "runtime.successCodes")]
        [InlineData("reboot", "runtime.rebootCodes")]
        [InlineData("retry", "runtime.retryCodes")]
        public void TheInstallerRestartCodeIsRejectedInEveryList(string list, string field)
        {
            var config = ValidMsi();
            if (list == "success")
            {
                config.Runtime.SuccessCodes.Add(1641);
            }
            else if (list == "reboot")
            {
                config.Runtime.RebootCodes.Add(1641);
            }
            else
            {
                config.Runtime.RetryCodes.Add(1641);
            }

            var issues = ConfigurationValidator.Validate(config);

            Assert.Contains(issues, i => i.Field == field && i.Code == ValidationCode.ForcedRestartCodeNotAllowed);
        }

        [Fact]
        public void TheDefaultCodesAreAccepted()
        {
            Assert.Empty(ConfigurationValidator.Validate(ValidMsi()));
        }

        [Fact]
        public void AtomicFile_WritesTheBomWhenTheEncodingHasOne()
        {
            var path = Path.Combine(_root, "with-bom.csv");

            AtomicFile.WriteAllText(path, "Key,Value\r\n\u00e4", new UTF8Encoding(true));

            var bytes = File.ReadAllBytes(path);
            Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3).ToArray());
            Assert.Equal("Key,Value\r\n\u00e4", new UTF8Encoding(true).GetString(bytes, 3, bytes.Length - 3));
        }

        [Fact]
        public void AtomicFile_StillWritesNoBomByDefault()
        {
            var path = Path.Combine(_root, "no-bom.json");

            AtomicFile.WriteAllText(path, "{}");

            Assert.Equal(new byte[] { (byte)'{', (byte)'}' }, File.ReadAllBytes(path));
        }
    }
}
