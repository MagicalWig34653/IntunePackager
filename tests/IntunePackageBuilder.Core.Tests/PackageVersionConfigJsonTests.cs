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
        public void RepeatedLoadAndSave_DoesNotGrowDefaultLists()
        {
            var json = JsonConvert.SerializeObject(Sample(), JsonFormat.CreateSettings());

            for (var i = 0; i < 3; i++)
            {
                var config = JsonConvert.DeserializeObject<PackageVersionConfig>(json, JsonFormat.CreateSettings());
                json = JsonConvert.SerializeObject(config, JsonFormat.CreateSettings());
            }

            var result = JsonConvert.DeserializeObject<PackageVersionConfig>(json, JsonFormat.CreateSettings());
            Assert.Equal(new[] { 0 }, result.Runtime.SuccessCodes.ToArray());
            Assert.Equal(new[] { 3010 }, result.Runtime.RebootCodes.ToArray());
            Assert.Equal(new[] { 1618 }, result.Runtime.RetryCodes.ToArray());
            Assert.Equal(new[] { "reader.exe" }, result.Interaction.ProcessesToClose.ToArray());
            Assert.Single(result.PostInstall.SharedShortcutsToRemove);
        }

        [Fact]
        public void StoredListsReplaceTheDefaults()
        {
            const string json = "{\"schemaVersion\":1,\"runtime\":{\"successCodes\":[0,3011],\"rebootCodes\":[],\"retryCodes\":[1618,1619]}}";

            var config = JsonConvert.DeserializeObject<PackageVersionConfig>(json, JsonFormat.CreateSettings());

            Assert.Equal(new[] { 0, 3011 }, config.Runtime.SuccessCodes.ToArray());
            Assert.Empty(config.Runtime.RebootCodes);
            Assert.Equal(new[] { 1618, 1619 }, config.Runtime.RetryCodes.ToArray());
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

        [Fact]
        public void ToolkitSettingsRoundTripAndAreWrittenInReadableForm()
        {
            var config = Sample();
            config.Deployment.Engine = DeploymentEngine.Psadt;
            config.Deployment.Psadt.DialogStyle = PsadtDialogStyle.Classic;
            config.Deployment.Psadt.AccentColor = "#0078D4";
            config.Deployment.Psadt.LogoFile = "logo.png";
            config.Deployment.Psadt.AllowDefer = true;
            config.Deployment.Psadt.DeferTimes = 5;

            var json = JsonConvert.SerializeObject(config, JsonFormat.CreateSettings());
            var parsed = JObject.Parse(json);
            Assert.Equal("Psadt", (string)parsed["deployment"]["engine"]);
            Assert.Equal("Classic", (string)parsed["deployment"]["psadt"]["dialogStyle"]);

            var copy = JsonConvert.DeserializeObject<PackageVersionConfig>(json, JsonFormat.CreateSettings());
            Assert.Equal(DeploymentEngine.Psadt, copy.Deployment.Engine);
            Assert.Equal(PsadtDialogStyle.Classic, copy.Deployment.Psadt.DialogStyle);
            Assert.Equal("#0078D4", copy.Deployment.Psadt.AccentColor);
            Assert.Equal("logo.png", copy.Deployment.Psadt.LogoFile);
            Assert.True(copy.Deployment.Psadt.AllowDefer);
            Assert.Equal(5, copy.Deployment.Psadt.DeferTimes);
            Assert.True(copy.Deployment.Psadt.ShowProgress);
        }

        [Fact]
        public void AFileWrittenBeforeTheDeploymentSectionExistedReadsAsTheNativeWrapper()
        {
            const string json = "{\"schemaVersion\":1,\"projectId\":\"contoso-reader\"}";

            var config = JsonConvert.DeserializeObject<PackageVersionConfig>(json, JsonFormat.CreateSettings());

            Assert.Equal(DeploymentEngine.Native, config.Deployment.Engine);
            Assert.Equal(PsadtDialogStyle.Fluent, config.Deployment.Psadt.DialogStyle);
            Assert.True(config.Deployment.Psadt.BalloonNotifications);
            Assert.Equal(3, config.Deployment.Psadt.DeferTimes);
        }
    }
}
