using System;
using System.IO;
using System.Linq;
using IntunePackageBuilder.Core.Builds;
using IntunePackageBuilder.Core.Sources;
using IntunePackageBuilder.Core.Storage;
using IntunePackageBuilder.Core.Versions;
using Newtonsoft.Json.Linq;
using Xunit;

namespace IntunePackageBuilder.Core.Tests
{
    public sealed class BuildSnapshotTests : IDisposable
    {
        private static readonly DateTime Created = new DateTime(2026, 10, 8, 15, 34, 12, DateTimeKind.Utc);

        private readonly string _root = Path.Combine(Path.GetTempPath(), "ipb-snapshot-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, true);
            }
        }

        private static PackageVersionConfig Config()
        {
            var config = PackageVersionConfig.CreateDefault("contoso-reader", InstallerType.Msi);
            config.Identity.SoftwareName = "Contoso Reader";
            config.Identity.Manufacturer = "Contoso Ltd.";
            config.Identity.TargetVersion = "4.2.1";
            config.Source.InstallerRelativePath = "setup.msi";
            config.Install.ProductCode = "{8F2C41A7-5B3E-4D19-A7C0-3E6D21B94F05}";
            config.Detection.MinimumVersion = "4.2.1";
            config.Interaction.ProcessesToClose.Add("reader.exe");
            return config;
        }

        private static SourceManifest Manifest(params string[] contents)
        {
            var manifest = new SourceManifest { CreatedUtc = Created };
            for (var i = 0; i < contents.Length; i++)
            {
                manifest.Files.Add(new SourceFileEntry { Path = "file" + i + ".dat", Size = contents[i].Length, Sha256 = contents[i] });
            }

            return manifest;
        }

        // ---- BuildId ----

        [Fact]
        public void BuildId_HasTheDocumentedShape()
        {
            var id = BuildId.New(new DateTime(2026, 10, 8, 15, 34, 12, DateTimeKind.Utc));

            Assert.StartsWith("20261008-153412-", id);
            Assert.True(BuildId.IsValid(id));
            Assert.Equal(20, id.Length);
        }

        [Fact]
        public void BuildId_UsesUtcAndTwoIdsInTheSameSecondUsuallyDiffer()
        {
            var local = new DateTime(2026, 10, 8, 15, 34, 12, DateTimeKind.Utc).ToLocalTime();

            Assert.StartsWith("20261008-153412-", BuildId.New(local));
            var ids = Enumerable.Range(0, 50).Select(i => BuildId.New(Created)).Distinct().Count();
            Assert.True(ids > 25, "The random suffix should separate ids created in the same second.");
        }

        [Theory]
        [InlineData("20261008-153412-a3f9", true)]
        [InlineData("20261008-153412-A3F9", false)]
        [InlineData("20261008-153412", false)]
        [InlineData("20261008-153412-a3f", false)]
        [InlineData("..\\20261008-153412-a3f9", false)]
        [InlineData("20261008-153412-a3f9\\x", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void BuildId_IsValid(string value, bool expected)
        {
            Assert.Equal(expected, BuildId.IsValid(value));
        }

        // ---- Snapshot ----

        [Fact]
        public void Create_RecordsTheBuildFacts()
        {
            var snapshot = BuildSnapshot.Create(Config(), Manifest("a", "b"), "20261008-153412-a3f9", "de", Created);

            Assert.Equal(BuildSnapshot.CurrentSchemaVersion, snapshot.SchemaVersion);
            Assert.Equal("20261008-153412-a3f9", snapshot.BuildId);
            Assert.Equal("de", snapshot.Language);
            Assert.Equal(Created, snapshot.CreatedUtc);
            Assert.Equal(AppInfo.Version, snapshot.ToolVersion);
            Assert.Equal(64, snapshot.SourceFingerprint.Length);
            Assert.Equal("Contoso Reader", snapshot.Configuration.Identity.SoftwareName);
        }

        [Fact]
        public void Create_TakesAPrivateCopyOfTheConfiguration()
        {
            var config = Config();

            var snapshot = BuildSnapshot.Create(config, Manifest("a"), "20261008-153412-a3f9", "en", Created);
            config.Identity.SoftwareName = "Changed afterwards";
            config.Interaction.ProcessesToClose.Add("other.exe");
            config.Runtime.SuccessCodes.Add(42);

            Assert.Equal("Contoso Reader", snapshot.Configuration.Identity.SoftwareName);
            Assert.Equal(new[] { "reader.exe" }, snapshot.Configuration.Interaction.ProcessesToClose.ToArray());
            Assert.Equal(new[] { 0 }, snapshot.Configuration.Runtime.SuccessCodes.ToArray());
        }

        [Fact]
        public void Create_RejectsInvalidInput()
        {
            var manifest = Manifest("a");

            Assert.Throws<ArgumentNullException>(() => BuildSnapshot.Create(null, manifest, "20261008-153412-a3f9", "en", Created));
            Assert.Throws<ArgumentNullException>(() => BuildSnapshot.Create(Config(), null, "20261008-153412-a3f9", "en", Created));
            Assert.Throws<ArgumentException>(() => BuildSnapshot.Create(Config(), manifest, "not-an-id", "en", Created));
            Assert.Throws<ArgumentException>(() => BuildSnapshot.Create(Config(), manifest, "20261008-153412-a3f9", "fr", Created));
            Assert.Throws<ArgumentException>(() => BuildSnapshot.Create(Config(), manifest, "20261008-153412-a3f9", null, Created));
        }

        // ---- Store ----

        [Fact]
        public void Store_WritesAndReadsTheSnapshot()
        {
            var file = Path.Combine(_root, "builds", "20261008-153412-a3f9", BuildSnapshot.FileName);
            var snapshot = BuildSnapshot.Create(Config(), Manifest("a"), "20261008-153412-a3f9", "de", Created);

            BuildSnapshotStore.WriteNew(file, snapshot);
            var loaded = BuildSnapshotStore.Read(file);

            Assert.Equal(snapshot.BuildId, loaded.BuildId);
            Assert.Equal("de", loaded.Language);
            Assert.Equal(snapshot.SourceFingerprint, loaded.SourceFingerprint);
            Assert.Equal(Created.Ticks, loaded.CreatedUtc.Ticks);
            Assert.Equal("{8F2C41A7-5B3E-4D19-A7C0-3E6D21B94F05}", loaded.Configuration.Install.ProductCode);
            Assert.Equal(new[] { "reader.exe" }, loaded.Configuration.Interaction.ProcessesToClose.ToArray());
        }

        [Fact]
        public void Store_NeverReplacesAnExistingSnapshot()
        {
            var file = Path.Combine(_root, BuildSnapshot.FileName);
            BuildSnapshotStore.WriteNew(file, BuildSnapshot.Create(Config(), Manifest("a"), "20261008-153412-a3f9", "en", Created));
            var before = File.ReadAllText(file);
            var other = Config();
            other.Identity.SoftwareName = "Different";

            Assert.Throws<IOException>(() => BuildSnapshotStore.WriteNew(file, BuildSnapshot.Create(other, Manifest("a"), "20261009-080000-0001", "en", Created)));

            Assert.Equal(before, File.ReadAllText(file));
        }

        [Fact]
        public void Store_UsesCamelCaseNames()
        {
            var file = Path.Combine(_root, BuildSnapshot.FileName);
            BuildSnapshotStore.WriteNew(file, BuildSnapshot.Create(Config(), Manifest("a"), "20261008-153412-a3f9", "en", Created));

            var document = JObject.Parse(File.ReadAllText(file));

            Assert.Equal(1, document.Value<int>("schemaVersion"));
            Assert.Equal("20261008-153412-a3f9", document.Value<string>("buildId"));
            Assert.Equal("en", document.Value<string>("language"));
            Assert.NotNull(document["configuration"]);
            Assert.NotNull(document["sourceFingerprint"]);
        }

        [Fact]
        public void Store_RejectsANewerSchemaAndAnIncompleteFile()
        {
            Directory.CreateDirectory(_root);
            var newer = Path.Combine(_root, "newer.json");
            File.WriteAllText(newer, "{\"schemaVersion\":99}");
            var incomplete = Path.Combine(_root, "incomplete.json");
            File.WriteAllText(incomplete, "{\"schemaVersion\":1,\"buildId\":\"20261008-153412-a3f9\"}");

            Assert.Throws<UnsupportedSchemaException>(() => BuildSnapshotStore.Read(newer));
            Assert.Throws<StorageFormatException>(() => BuildSnapshotStore.Read(incomplete));
        }

        // ---- Fingerprint ----

        [Fact]
        public void Fingerprint_IsDeterministicAndIgnoresTheTimestamp()
        {
            var first = Manifest("a", "b");
            var second = Manifest("a", "b");
            second.CreatedUtc = Created.AddDays(30);

            Assert.Equal(first.ComputeFingerprint(), second.ComputeFingerprint());
            Assert.Matches("^[0-9a-f]{64}$", first.ComputeFingerprint());
        }

        [Fact]
        public void Fingerprint_ChangesWithAnyFileChange()
        {
            var baseline = Manifest("a", "b").ComputeFingerprint();

            Assert.NotEqual(baseline, Manifest("a", "c").ComputeFingerprint());
            Assert.NotEqual(baseline, Manifest("a").ComputeFingerprint());
            var renamed = Manifest("a", "b");
            renamed.Files[0].Path = "renamed.dat";
            Assert.NotEqual(baseline, renamed.ComputeFingerprint());
            var resized = Manifest("a", "b");
            resized.Files[0].Size = 99;
            Assert.NotEqual(baseline, resized.ComputeFingerprint());
        }

        [Fact]
        public void Fingerprint_DoesNotDependOnTheOrderOfTheEntries()
        {
            var forward = Manifest("a", "b", "c");
            var reversed = Manifest("a", "b", "c");
            reversed.Files.Reverse();

            Assert.Equal(forward.ComputeFingerprint(), reversed.ComputeFingerprint());
        }
    }
}
