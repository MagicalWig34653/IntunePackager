using System;
using System.Linq;
using IntunePackageBuilder.Core.Projects;
using IntunePackageBuilder.Core.Settings;
using Xunit;

namespace IntunePackageBuilder.Core.Tests
{
    public class AppSettingsTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc);

        [Fact]
        public void NewSettings_AreEmptyWithoutABaseFolder()
        {
            var settings = new AppSettings();

            Assert.Equal(AppSettings.CurrentSchemaVersion, settings.SchemaVersion);
            Assert.Null(settings.BaseFolder);
            Assert.Empty(settings.RecentProjects);
        }

        [Fact]
        public void MarkOpened_PutsTheNewestProjectFirst()
        {
            var settings = new AppSettings();

            settings.MarkOpened("alpha", T0);
            settings.MarkOpened("beta", T0.AddMinutes(1));

            Assert.Equal(new[] { "beta", "alpha" }, settings.RecentProjects.Select(r => r.ProjectId).ToArray());
            Assert.Equal(T0.AddMinutes(1), settings.RecentProjects[0].LastOpenedUtc);
        }

        [Fact]
        public void MarkOpened_MovesAnAlreadyListedProjectToTheTopWithoutDuplicating()
        {
            var settings = new AppSettings();
            settings.MarkOpened("alpha", T0);
            settings.MarkOpened("beta", T0.AddMinutes(1));

            settings.MarkOpened("alpha", T0.AddMinutes(2));

            Assert.Equal(new[] { "alpha", "beta" }, settings.RecentProjects.Select(r => r.ProjectId).ToArray());
            Assert.Equal(T0.AddMinutes(2), settings.RecentProjects[0].LastOpenedUtc);
        }

        [Fact]
        public void MarkOpened_KeepsOnlyTheMostRecentProjects()
        {
            var settings = new AppSettings();
            for (var i = 0; i < AppSettings.MaxRecentProjects + 5; i++)
            {
                settings.MarkOpened("project-" + i, T0.AddMinutes(i));
            }

            Assert.Equal(AppSettings.MaxRecentProjects, settings.RecentProjects.Count);
            Assert.Equal("project-" + (AppSettings.MaxRecentProjects + 4), settings.RecentProjects[0].ProjectId);
            Assert.DoesNotContain(settings.RecentProjects, r => r.ProjectId == "project-0");
        }

        [Theory]
        [InlineData("")]
        [InlineData("Has Space")]
        [InlineData("../escape")]
        [InlineData("con")]
        public void MarkOpened_RejectsInvalidIds(string id)
        {
            Assert.Throws<InvalidProjectIdException>(() => new AppSettings().MarkOpened(id, T0));
        }

        [Fact]
        public void RemoveRecent_DropsAProjectAndIgnoresUnknownOnes()
        {
            var settings = new AppSettings();
            settings.MarkOpened("alpha", T0);
            settings.MarkOpened("beta", T0.AddMinutes(1));

            settings.RemoveRecent("alpha");
            settings.RemoveRecent("unknown");

            Assert.Equal(new[] { "beta" }, settings.RecentProjects.Select(r => r.ProjectId).ToArray());
        }
    }
}
