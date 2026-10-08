using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using IntunePackageBuilder.Core.Projects;
using IntunePackageBuilder.Core.Storage;
using Newtonsoft.Json.Linq;
using Xunit;

namespace IntunePackageBuilder.Core.Tests
{
    public sealed class ProjectStoreTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "ipb-projects-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, true);
            }
        }

        private ProjectStore NewStore()
        {
            return new ProjectStore(_root);
        }

        private sealed class AppendNotesStep : IMigrationStep
        {
            public int FromVersion
            {
                get { return 1; }
            }

            public void Apply(JObject document)
            {
                document["notes"] = "migrated";
            }
        }

        [Fact]
        public void Create_WritesProjectFileAndLoadReturnsTheSameValues()
        {
            var created = NewStore().Create("contoso-reader", "  Contoso Reader  ");

            var loaded = NewStore().Load("contoso-reader");

            Assert.Equal(Project.CurrentSchemaVersion, loaded.SchemaVersion);
            Assert.Equal("contoso-reader", loaded.ProjectId);
            Assert.Equal("Contoso Reader", loaded.DisplayName);
            Assert.Equal(string.Empty, loaded.Notes);
            Assert.Equal(created.CreatedUtc.Ticks, loaded.CreatedUtc.Ticks);
            Assert.True(File.Exists(Path.Combine(_root, "contoso-reader", "project.json")));
        }

        [Fact]
        public void Create_RejectsDuplicateIds()
        {
            var store = NewStore();
            store.Create("app", "App");

            Assert.Throws<ProjectExistsException>(() => store.Create("app", "Other"));
        }

        [Theory]
        [InlineData("../escape")]
        [InlineData("..")]
        [InlineData("Has Space")]
        [InlineData("con")]
        [InlineData("")]
        public void Create_RejectsInvalidIds(string id)
        {
            Assert.Throws<InvalidProjectIdException>(() => NewStore().Create(id, "Name"));
        }

        [Fact]
        public void Load_ReportsMissingProjects()
        {
            Assert.Throws<ProjectNotFoundException>(() => NewStore().Load("missing"));
        }

        [Fact]
        public void Notes_SurviveLeavingAndReopeningTheProject()
        {
            var notes = "Line 1\r\nLine 2\n\ttabbed \"quoted\" back\\slash {\"json\":1}\n"
                + "2026-10-08T10:00:00Z\n"
                + "umlauts \u00e4\u00f6\u00fc\u00df euro \u20ac\n"
                + "  leading and trailing spaces  ";
            NewStore().Create("app", "App");

            NewStore().SaveNotes("app", notes);
            var reopened = NewStore().Load("app");

            Assert.Equal(notes, reopened.Notes);
        }

        [Fact]
        public void SwitchingBetweenProjects_NeverMixesData()
        {
            var store = NewStore();
            store.Create("alpha", "Alpha");
            store.Create("beta", "Beta");
            store.SaveNotes("alpha", "notes of alpha");
            store.SaveNotes("beta", "notes of beta");

            for (var i = 0; i < 3; i++)
            {
                var alpha = store.Load("alpha");
                var beta = store.Load("beta");

                Assert.Equal("Alpha", alpha.DisplayName);
                Assert.Equal("notes of alpha", alpha.Notes);
                Assert.Equal("Beta", beta.DisplayName);
                Assert.Equal("notes of beta", beta.Notes);
            }
        }

        [Fact]
        public void List_ReportsBrokenProjectsWithoutAffectingTheOthers()
        {
            var store = NewStore();
            store.Create("good-one", "Good One");
            store.Create("good-two", "Good Two");
            Directory.CreateDirectory(Path.Combine(_root, "broken-json"));
            File.WriteAllText(Path.Combine(_root, "broken-json", "project.json"), "{ not json");
            Directory.CreateDirectory(Path.Combine(_root, "from-the-future"));
            File.WriteAllText(Path.Combine(_root, "from-the-future", "project.json"),
                "{\"schemaVersion\":99,\"projectId\":\"from-the-future\",\"displayName\":\"F\"}");
            Directory.CreateDirectory(Path.Combine(_root, "Bad Name"));
            File.WriteAllText(Path.Combine(_root, "Bad Name", "project.json"), "{}");
            Directory.CreateDirectory(Path.Combine(_root, "no-project-file"));

            var entries = store.List();

            Assert.Equal(new[] { "Bad Name", "broken-json", "from-the-future", "good-one", "good-two" },
                entries.Select(e => e.FolderName).ToArray());
            Assert.IsType<InvalidProjectIdException>(entries.Single(e => e.FolderName == "Bad Name").Error);
            Assert.IsType<StorageFormatException>(entries.Single(e => e.FolderName == "broken-json").Error);
            Assert.IsType<UnsupportedSchemaException>(entries.Single(e => e.FolderName == "from-the-future").Error);
            Assert.Equal("Good One", entries.Single(e => e.FolderName == "good-one").Project.DisplayName);
            Assert.Equal("Good Two", entries.Single(e => e.FolderName == "good-two").Project.DisplayName);
        }

        [Fact]
        public void List_ReturnsEmptyWhenBaseFolderDoesNotExist()
        {
            Assert.Empty(NewStore().List());
        }

        [Fact]
        public void Load_RejectsProjectWhoseIdDoesNotMatchItsFolder()
        {
            Directory.CreateDirectory(Path.Combine(_root, "folder-a"));
            File.WriteAllText(Path.Combine(_root, "folder-a", "project.json"),
                "{\"schemaVersion\":1,\"projectId\":\"folder-b\",\"displayName\":\"X\"}");

            Assert.Throws<StorageFormatException>(() => NewStore().Load("folder-a"));
        }

        [Fact]
        public void Save_RefusesToOverwriteNewerSchemaAndKeepsTheFile()
        {
            Directory.CreateDirectory(Path.Combine(_root, "app"));
            var file = Path.Combine(_root, "app", "project.json");
            const string future = "{\"schemaVersion\":99,\"projectId\":\"app\",\"displayName\":\"Future\"}";
            File.WriteAllText(file, future);
            var project = new Project { ProjectId = "app", DisplayName = "Old program", CreatedUtc = DateTime.UtcNow, Notes = "n" };

            Assert.Throws<UnsupportedSchemaException>(() => NewStore().Save(project));

            Assert.Equal(future, File.ReadAllText(file));
        }

        [Fact]
        public void Load_MigratesOldFilesAndKeepsABackup()
        {
            Directory.CreateDirectory(Path.Combine(_root, "app"));
            var file = Path.Combine(_root, "app", "project.json");
            const string original = "{\"schemaVersion\":1,\"projectId\":\"app\",\"displayName\":\"App\",\"createdUtc\":\"2026-01-01T00:00:00Z\",\"notes\":\"old\"}";
            File.WriteAllText(file, original);
            var store = new ProjectStore(_root, 2, new List<IMigrationStep> { new AppendNotesStep() });

            var project = store.Load("app");

            Assert.Equal("migrated", project.Notes);
            Assert.Equal(2, project.SchemaVersion);
            Assert.Equal(2, JObject.Parse(File.ReadAllText(file)).Value<int>("schemaVersion"));
            Assert.Equal(original, File.ReadAllText(file + ".v1.bak"));
        }

        [Fact]
        public void Save_LeavesNoTemporaryFilesBehind()
        {
            var store = NewStore();
            store.Create("app", "App");
            store.SaveNotes("app", "x");
            store.SaveNotes("app", "y");

            var files = Directory.GetFiles(Path.Combine(_root, "app")).Select(Path.GetFileName).ToArray();

            Assert.Equal(new[] { "project.json" }, files);
        }
    }
}
