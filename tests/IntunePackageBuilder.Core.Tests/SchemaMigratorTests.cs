using System.Collections.Generic;
using IntunePackageBuilder.Core.Storage;
using Newtonsoft.Json.Linq;
using Xunit;

namespace IntunePackageBuilder.Core.Tests
{
    public class SchemaMigratorTests
    {
        private sealed class AddFieldStep : IMigrationStep
        {
            private readonly string _name;

            public AddFieldStep(int fromVersion, string name)
            {
                FromVersion = fromVersion;
                _name = name;
            }

            public int FromVersion { get; private set; }

            public void Apply(JObject document)
            {
                document[_name] = true;
            }
        }

        private static readonly IMigrationStep[] NoSteps = new IMigrationStep[0];

        [Fact]
        public void Migrate_DoesNothingWhenVersionIsCurrent()
        {
            var document = JObject.Parse("{\"schemaVersion\":2}");

            var result = SchemaMigrator.Migrate("f.json", document, 2, NoSteps);

            Assert.False(result.Migrated);
            Assert.Equal(2, result.ToVersion);
        }

        [Fact]
        public void Migrate_AppliesStepsInOrderAndUpdatesVersion()
        {
            var document = JObject.Parse("{\"schemaVersion\":1}");
            var steps = new List<IMigrationStep> { new AddFieldStep(2, "b"), new AddFieldStep(1, "a") };

            var result = SchemaMigrator.Migrate("f.json", document, 3, steps);

            Assert.True(result.Migrated);
            Assert.Equal(1, result.FromVersion);
            Assert.Equal(3, result.ToVersion);
            Assert.Equal(3, document.Value<int>("schemaVersion"));
            Assert.True(document.Value<bool>("a"));
            Assert.True(document.Value<bool>("b"));
        }

        [Fact]
        public void Migrate_RejectsNewerDocuments()
        {
            var document = JObject.Parse("{\"schemaVersion\":9}");

            var ex = Assert.Throws<UnsupportedSchemaException>(() => SchemaMigrator.Migrate("f.json", document, 2, NoSteps));

            Assert.Equal(9, ex.FoundVersion);
            Assert.Equal(2, ex.SupportedVersion);
            Assert.Equal(9, document.Value<int>("schemaVersion"));
        }

        [Fact]
        public void Migrate_FailsWhenAStepIsMissing()
        {
            var document = JObject.Parse("{\"schemaVersion\":1}");

            var ex = Assert.Throws<MigrationException>(() => SchemaMigrator.Migrate("f.json", document, 2, NoSteps));

            Assert.Equal(1, ex.FromVersion);
        }

        [Theory]
        [InlineData("{}")]
        [InlineData("{\"schemaVersion\":\"1\"}")]
        [InlineData("{\"schemaVersion\":0}")]
        [InlineData("{\"schemaVersion\":-3}")]
        [InlineData("{\"schemaVersion\":1.5}")]
        public void Migrate_RejectsMissingOrInvalidVersion(string json)
        {
            Assert.Throws<StorageFormatException>(() => SchemaMigrator.Migrate("f.json", JObject.Parse(json), 1, NoSteps));
        }
    }
}
