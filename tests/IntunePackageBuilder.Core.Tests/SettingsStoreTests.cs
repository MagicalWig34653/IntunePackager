using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using IntunePackageBuilder.Core.Settings;
using IntunePackageBuilder.Core.Storage;
using Newtonsoft.Json.Linq;
using Xunit;

namespace IntunePackageBuilder.Core.Tests
{
    public sealed class SettingsStoreTests : IDisposable
    {
        private static readonly DateTime T0 = new DateTime(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc);

        private readonly string _root = Path.Combine(Path.GetTempPath(), "ipb-settings-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, true);
            }
        }

        private string SettingsFile
        {
            get { return Path.Combine(_root, "settings.json"); }
        }

        private SettingsStore NewStore()
        {
            return new SettingsStore(_root);
        }

        private sealed class SetBaseFolderStep : IMigrationStep
        {
            public int FromVersion
            {
                get { return 1; }
            }

            public void Apply(JObject document)
            {
                document["baseFolder"] = "C:\\migrated";
            }
        }

        [Fact]
        public void Load_ReturnsDefaultsWithoutErrorWhenThereIsNoFile()
        {
            var result = NewStore().Load();

            Assert.Null(result.Error);
            Assert.Null(result.Settings.BaseFolder);
            Assert.Empty(result.Settings.RecentProjects);
            Assert.False(File.Exists(SettingsFile));
        }

        [Fact]
        public void SaveAndLoad_RoundTripBaseFolderAndRecentProjects()
        {
            var settings = new AppSettings { BaseFolder = "D:\\Projekte \u00e4\u00f6\u00fc\\Intune" };
            settings.MarkOpened("alpha", T0);
            settings.MarkOpened("beta", T0.AddMinutes(1));

            NewStore().Save(settings);
            var loaded = NewStore().Load();

            Assert.Null(loaded.Error);
            Assert.Equal("D:\\Projekte \u00e4\u00f6\u00fc\\Intune", loaded.Settings.BaseFolder);
            Assert.Equal(new[] { "beta", "alpha" }, loaded.Settings.RecentProjects.Select(r => r.ProjectId).ToArray());
            Assert.Equal(T0.AddMinutes(1).Ticks, loaded.Settings.RecentProjects[0].LastOpenedUtc.Ticks);
        }

        [Fact]
        public void Save_LeavesOnlyTheSettingsFile()
        {
            var store = NewStore();
            store.Save(new AppSettings());
            store.Save(new AppSettings { BaseFolder = "C:\\x" });

            Assert.Equal(new[] { "settings.json" }, Directory.GetFiles(_root).Select(Path.GetFileName).ToArray());
        }

        [Fact]
        public void Load_ReportsADamagedFileAndFallsBackToDefaultsWithoutTouchingIt()
        {
            Directory.CreateDirectory(_root);
            File.WriteAllText(SettingsFile, "{ not json");

            var result = NewStore().Load();

            Assert.IsType<StorageFormatException>(result.Error);
            Assert.Null(result.Settings.BaseFolder);
            Assert.Equal("{ not json", File.ReadAllText(SettingsFile));
        }

        [Fact]
        public void Save_KeepsADamagedFileAsBackupBeforeReplacingIt()
        {
            Directory.CreateDirectory(_root);
            File.WriteAllText(SettingsFile, "{ not json");

            NewStore().Save(new AppSettings { BaseFolder = "C:\\x" });

            var backups = Directory.GetFiles(_root, "settings.json.invalid-*.bak");
            Assert.Single(backups);
            Assert.Equal("{ not json", File.ReadAllText(backups[0]));
            Assert.Equal("C:\\x", NewStore().Load().Settings.BaseFolder);
        }

        [Fact]
        public void Load_ReportsANewerFileAndSaveRefusesToOverwriteIt()
        {
            Directory.CreateDirectory(_root);
            const string future = "{\"schemaVersion\":99,\"baseFolder\":\"C:\\\\future\"}";
            File.WriteAllText(SettingsFile, future);
            var store = NewStore();

            var result = store.Load();

            Assert.IsType<UnsupportedSchemaException>(result.Error);
            Assert.Throws<UnsupportedSchemaException>(() => store.Save(new AppSettings()));
            Assert.Equal(future, File.ReadAllText(SettingsFile));
        }

        [Fact]
        public void Load_DropsInvalidAndDuplicateRecentProjects()
        {
            Directory.CreateDirectory(_root);
            File.WriteAllText(SettingsFile,
                "{\"schemaVersion\":1,\"recentProjects\":["
                + "{\"projectId\":\"alpha\"},{\"projectId\":\"Bad Id\"},{\"projectId\":\"alpha\"},{\"projectId\":\"beta\"},{\"projectId\":\"\"}]}");

            var result = NewStore().Load();

            Assert.Null(result.Error);
            Assert.Equal(new[] { "alpha", "beta" }, result.Settings.RecentProjects.Select(r => r.ProjectId).ToArray());
        }

        [Fact]
        public void Load_MigratesOldFilesAndKeepsABackup()
        {
            Directory.CreateDirectory(_root);
            const string original = "{\"schemaVersion\":1,\"baseFolder\":\"C:\\\\old\"}";
            File.WriteAllText(SettingsFile, original);
            var store = new SettingsStore(_root, 2, new List<IMigrationStep> { new SetBaseFolderStep() });

            var result = store.Load();

            Assert.Null(result.Error);
            Assert.Equal("C:\\migrated", result.Settings.BaseFolder);
            Assert.Equal(original, File.ReadAllText(SettingsFile + ".v1.bak"));
        }

        [Fact]
        public void DefaultDirectory_IsBelowLocalAppData()
        {
            var expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Intune Package Builder");

            Assert.Equal(expected, SettingsStore.DefaultDirectory());
        }

        [Fact]
        public void TheProgramModeIsNotStored()
        {
            NewStore().Save(new AppSettings());

            var names = JObject.Parse(File.ReadAllText(SettingsFile)).Properties().Select(p => p.Name).ToArray();

            Assert.DoesNotContain(names, n => n.IndexOf("mode", StringComparison.OrdinalIgnoreCase) >= 0);
        }
    }
}
