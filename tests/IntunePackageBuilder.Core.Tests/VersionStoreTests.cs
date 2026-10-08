using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using IntunePackageBuilder.Core.Projects;
using IntunePackageBuilder.Core.Storage;
using IntunePackageBuilder.Core.Versions;
using Newtonsoft.Json.Linq;
using Xunit;

namespace IntunePackageBuilder.Core.Tests
{
    public sealed class VersionStoreTests : IDisposable
    {
        private const string ProductCode = "{8F2C41A7-5B3E-4D19-A7C0-3E6D21B94F05}";

        private readonly string _root = Path.Combine(Path.GetTempPath(), "ipb-versions-" + Guid.NewGuid().ToString("N"));
        private readonly ProjectStore _projects;
        private readonly VersionStore _versions;

        public VersionStoreTests()
        {
            _projects = new ProjectStore(_root);
            _projects.Create("contoso-reader", "Contoso Reader");
            _versions = new VersionStore(_projects);
        }

        public void Dispose()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, true);
            }
        }

        private sealed class MarkStep : IMigrationStep
        {
            public int FromVersion
            {
                get { return 1; }
            }

            public void Apply(JObject document)
            {
                document["interaction"] = new JObject { { "detailMessage", "migrated" } };
            }
        }

        private static PackageVersionConfig Config(string version)
        {
            var config = PackageVersionConfig.CreateDefault("contoso-reader", InstallerType.Msi);
            config.Identity.SoftwareName = "Contoso Reader";
            config.Identity.Manufacturer = "Contoso Ltd.";
            config.Identity.TargetVersion = version;
            config.Source.InstallerRelativePath = "setup.msi";
            config.Install.ProductCode = ProductCode;
            config.Detection.MinimumVersion = version;
            return config;
        }

        private string ConfigFile(string folder)
        {
            return Path.Combine(_root, "contoso-reader", "versions", folder, "configuration.json");
        }

        [Fact]
        public void Create_StoresTheConfigurationAndLoadReturnsIt()
        {
            _versions.Create("contoso-reader", Config("4.2.1"));

            var loaded = _versions.Load("contoso-reader", "4.2.1");

            Assert.Equal("Contoso Reader", loaded.Identity.SoftwareName);
            Assert.Equal(ProductCode, loaded.Install.ProductCode);
            Assert.Equal(PackageVersionConfig.CurrentSchemaVersion, loaded.SchemaVersion);
            Assert.True(File.Exists(ConfigFile("4.2.1")));
        }

        [Fact]
        public void Create_LeavesNoLockFileBehind()
        {
            _versions.Create("contoso-reader", Config("4.2.1"));

            var files = Directory.GetFiles(Path.GetDirectoryName(ConfigFile("4.2.1"))).Select(Path.GetFileName).ToArray();

            Assert.Equal(new[] { "configuration.json" }, files);
        }

        [Fact]
        public void Create_AcceptsAnIncompleteDraft()
        {
            var draft = PackageVersionConfig.CreateDefault("contoso-reader", InstallerType.Exe);
            draft.Identity.TargetVersion = "1.0";

            _versions.Create("contoso-reader", draft);

            Assert.NotEmpty(ConfigurationValidator.Validate(_versions.Load("contoso-reader", "1.0")));
        }

        [Fact]
        public void Create_RejectsUnknownProjectsAndInvalidInput()
        {
            Assert.Throws<ProjectNotFoundException>(() => _versions.Create("missing", Config("1.0")));
            Assert.Throws<ArgumentException>(() => _versions.Create("contoso-reader", Config("v1")));
            Assert.Throws<ArgumentException>(() => _versions.Create("contoso-reader", Config("")));

            var foreign = Config("1.0");
            foreign.ProjectId = "someone-else";
            Assert.Throws<ArgumentException>(() => _versions.Create("contoso-reader", foreign));
        }

        [Fact]
        public void Create_RejectsANumericallyEqualVersion()
        {
            _versions.Create("contoso-reader", Config("1.0"));

            Assert.Throws<VersionExistsException>(() => _versions.Create("contoso-reader", Config("1.0")));
            Assert.Throws<VersionExistsException>(() => _versions.Create("contoso-reader", Config("1.0.0")));
        }

        [Fact]
        public void Load_FindsAVersionByNumericEquality()
        {
            _versions.Create("contoso-reader", Config("4.2.1"));

            Assert.Equal("4.2.1", _versions.Load("contoso-reader", "4.2.1.0").Identity.TargetVersion);
            Assert.Throws<VersionNotFoundException>(() => _versions.Load("contoso-reader", "9.9"));
        }

        [Fact]
        public void Save_UpdatesTheStoredConfiguration()
        {
            _versions.Create("contoso-reader", Config("4.2.1"));
            var config = _versions.Load("contoso-reader", "4.2.1");
            config.Interaction.DetailMessage = "Please save your work.";

            _versions.Save("contoso-reader", "4.2.1", config);

            Assert.Equal("Please save your work.", _versions.Load("contoso-reader", "4.2.1").Interaction.DetailMessage);
        }

        [Fact]
        public void Save_RefusesToChangeTheTargetVersion()
        {
            _versions.Create("contoso-reader", Config("4.2.1"));
            _versions.Create("contoso-reader", Config("5.0"));
            var config = _versions.Load("contoso-reader", "4.2.1");
            config.Identity.TargetVersion = "5.0";

            Assert.Throws<ArgumentException>(() => _versions.Save("contoso-reader", "4.2.1", config));

            Assert.Equal("5.0", _versions.Load("contoso-reader", "5.0").Identity.TargetVersion);
            Assert.Equal("4.2.1", _versions.Load("contoso-reader", "4.2.1").Identity.TargetVersion);
        }

        [Fact]
        public void Save_IsRefusedWhileAnotherHolderHasTheLock_AndKeepsTheFile()
        {
            _versions.Create("contoso-reader", Config("4.2.1"));
            var before = File.ReadAllText(ConfigFile("4.2.1"));
            var config = _versions.Load("contoso-reader", "4.2.1");
            config.Interaction.DetailMessage = "changed";

            using (VersionLock.Acquire(Path.GetDirectoryName(ConfigFile("4.2.1"))))
            {
                Assert.Throws<LockHeldException>(() => _versions.Save("contoso-reader", "4.2.1", config));
            }

            Assert.Equal(before, File.ReadAllText(ConfigFile("4.2.1")));
        }

        [Fact]
        public void Save_RefusesToOverwriteANewerSchema()
        {
            _versions.Create("contoso-reader", Config("4.2.1"));
            const string future = "{\"schemaVersion\":99,\"projectId\":\"contoso-reader\"}";
            File.WriteAllText(ConfigFile("4.2.1"), future);
            var config = Config("4.2.1");

            Assert.Throws<UnsupportedSchemaException>(() => _versions.Save("contoso-reader", "4.2.1", config));

            Assert.Equal(future, File.ReadAllText(ConfigFile("4.2.1")));
        }

        [Fact]
        public void List_SortsNumericallyNewestFirst()
        {
            foreach (var version in new[] { "1.2.0", "1.10.0", "1.9.5", "0.9" })
            {
                _versions.Create("contoso-reader", Config(version));
            }

            var names = _versions.List("contoso-reader").Select(e => e.FolderName).ToArray();

            Assert.Equal(new[] { "1.10.0", "1.9.5", "1.2.0", "0.9" }, names);
        }

        [Fact]
        public void List_ReportsBrokenVersionsWithoutAffectingTheOthers()
        {
            _versions.Create("contoso-reader", Config("2.0"));
            _versions.Create("contoso-reader", Config("1.0"));
            var versionsDir = Path.Combine(_root, "contoso-reader", "versions");
            Directory.CreateDirectory(Path.Combine(versionsDir, "3.0"));
            File.WriteAllText(Path.Combine(versionsDir, "3.0", "configuration.json"), "{ not json");
            Directory.CreateDirectory(Path.Combine(versionsDir, "4.0"));
            File.WriteAllText(Path.Combine(versionsDir, "4.0", "configuration.json"),
                "{\"schemaVersion\":1,\"projectId\":\"contoso-reader\",\"identity\":{\"targetVersion\":\"9.9\"}}");
            Directory.CreateDirectory(Path.Combine(versionsDir, "not-a-version"));
            File.WriteAllText(Path.Combine(versionsDir, "not-a-version", "configuration.json"), "{}");
            Directory.CreateDirectory(Path.Combine(versionsDir, "5.0"));

            var entries = _versions.List("contoso-reader");

            Assert.Equal(new[] { "4.0", "3.0", "2.0", "1.0", "not-a-version" }, entries.Select(e => e.FolderName).ToArray());
            Assert.IsType<StorageFormatException>(entries.Single(e => e.FolderName == "4.0").Error);
            Assert.IsType<StorageFormatException>(entries.Single(e => e.FolderName == "3.0").Error);
            Assert.IsType<StorageFormatException>(entries.Single(e => e.FolderName == "not-a-version").Error);
            Assert.Equal("2.0", entries.Single(e => e.FolderName == "2.0").Config.Identity.TargetVersion);
            Assert.Equal("1.0", entries.Single(e => e.FolderName == "1.0").Config.Identity.TargetVersion);
        }

        [Fact]
        public void List_IsEmptyWhenThereAreNoVersions()
        {
            Assert.Empty(_versions.List("contoso-reader"));
        }

        [Fact]
        public void Load_RejectsAConfigurationOfAnotherProject()
        {
            _versions.Create("contoso-reader", Config("1.0"));
            var json = JObject.Parse(File.ReadAllText(ConfigFile("1.0")));
            json["projectId"] = "someone-else";
            File.WriteAllText(ConfigFile("1.0"), json.ToString());

            Assert.Throws<StorageFormatException>(() => _versions.Load("contoso-reader", "1.0"));
        }

        [Fact]
        public void Load_MigratesOldFilesAndKeepsABackup()
        {
            _versions.Create("contoso-reader", Config("1.0"));
            var original = File.ReadAllText(ConfigFile("1.0"));
            var migrating = new VersionStore(_projects, 2, new List<IMigrationStep> { new MarkStep() });

            var loaded = migrating.Load("contoso-reader", "1.0");

            Assert.Equal("migrated", loaded.Interaction.DetailMessage);
            Assert.Equal(2, JObject.Parse(File.ReadAllText(ConfigFile("1.0"))).Value<int>("schemaVersion"));
            Assert.Equal(original, File.ReadAllText(ConfigFile("1.0") + ".v1.bak"));
        }
    }
}
