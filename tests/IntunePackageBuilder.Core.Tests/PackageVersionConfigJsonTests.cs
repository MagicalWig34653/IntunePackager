using IntunePackageBuilder.Core.Storage;
using IntunePackageBuilder.Core.Versions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace IntunePackageBuilder.Core.Tests
{
    public class PackageVersionConfigJsonTests
    {
        private static PackageVersionConfig Sample()
        {
            var config = PackageVersionConfig.CreateDefault("contoso-reader", InstallerType.Msi);
            config.Identity.SoftwareName = "Contoso \"Reader\" \u00e4";
            config.Identity.Manufacturer = "Contoso Ltd.";
            config.Identity.TargetVersion = "4.2.1";
            config.Source.InstallerRelativePath = "vendor\\setup.msi";
            config.Source.ImportKind = ImportKind.Folder;
            config.Install.ProductCode = "{8F2C41A7-5B3E-4D19-A7C0-3E6D21B94F05}";
            config.Install.TargetArchitecture = TargetArchitecture.X86;
            config.Detection.MinimumVersion = "4.2.1";
            config.Interaction.ProcessesToClose.Add("reader.exe");
            config.PostInstall.SharedShortcutsToRemove.Add(new SharedShortcut { Root = ShortcutRoot.PublicDesktop, RelativePath = "Reader.lnk" });
            return config;
        }

        [Fact]
        public void RoundTrip_PreservesAllValues()
        {
            var json = JsonConvert.SerializeObject(Sample(), JsonFormat.CreateSettings());

            var copy = JsonConvert.DeserializeObject<PackageVersionConfig>(json, JsonFormat.CreateSettings());

            Assert.Equal("Contoso \"Reader\" \u00e4", copy.Identity.SoftwareName);
            Assert.Equal("vendor\\setup.msi", copy.Source.InstallerRelativePath);
            Assert.Equal(ImportKind.Folder, copy.Source.ImportKind);
            Assert.Equal(TargetArchitecture.X86, copy.Install.TargetArchitecture);
            Assert.Equal(new[] { "reader.exe" }, copy.Interaction.ProcessesToClose.ToArray());
            Assert.Equal(ShortcutRoot.PublicDesktop, copy.PostInstall.SharedShortcutsToRemove[0].Root);
            Assert.Equal(new[] { 0 }, copy.Runtime.SuccessCodes.ToArray());
            Assert.Equal(PackageVersionConfig.CurrentSchemaVersion, copy.SchemaVersion);
        }

        [Fact]
        public void Serialization_UsesCamelCaseNamesAndStringEnums()
        {
            var document = JObject.Parse(JsonConvert.SerializeObject(Sample(), JsonFormat.CreateSettings()));

            Assert.Equal(1, document.Value<int>("schemaVersion"));
            Assert.Equal("Msi", document["source"].Value<string>("installerType"));
            Assert.Equal("MsiProductCode", document["detection"].Value<string>("method"));
            Assert.Equal("contoso-reader", document.Value<string>("projectId"));
        }
    }
}
