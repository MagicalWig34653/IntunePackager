using System;
using System.Collections.Generic;
using IntunePackageBuilder.Core.Projects;

namespace IntunePackageBuilder.Core.Settings
{
    public sealed class RecentProject
    {
        public string ProjectId { get; set; }

        public DateTime LastOpenedUtc { get; set; }
    }

    /// <summary>
    /// Content of the authoring tool's <c>settings.json</c>. The program mode (standard or advanced)
    /// is deliberately not stored: standard mode is active at every start (SPEC section 5.2).
    /// </summary>
    public sealed class AppSettings
    {
        public const int CurrentSchemaVersion = 1;
        public const string FileName = "settings.json";
        public const int MaxRecentProjects = 10;

        public AppSettings()
        {
            SchemaVersion = CurrentSchemaVersion;
            RecentProjects = new List<RecentProject>();
        }

        public int SchemaVersion { get; set; }

        /// <summary>The configured base folder, or null when the user has not chosen one yet.</summary>
        public string BaseFolder { get; set; }

        /// <summary>Most recently opened projects, newest first.</summary>
        public List<RecentProject> RecentProjects { get; set; }

        /// <summary>Moves a project to the top of the recent list (no duplicates, capped at <see cref="MaxRecentProjects"/>).</summary>
        public void MarkOpened(string projectId, DateTime openedUtc)
        {
            var problem = ProjectId.Check(projectId);
            if (problem != ProjectIdProblem.None)
            {
                throw new InvalidProjectIdException(projectId, problem);
            }

            RecentProjects.RemoveAll(r => string.Equals(r.ProjectId, projectId, StringComparison.Ordinal));
            RecentProjects.Insert(0, new RecentProject { ProjectId = projectId, LastOpenedUtc = openedUtc });
            Trim();
        }

        public void RemoveRecent(string projectId)
        {
            RecentProjects.RemoveAll(r => string.Equals(r.ProjectId, projectId, StringComparison.Ordinal));
        }

        /// <summary>Drops entries with invalid IDs and duplicates (for example from a hand-edited file).</summary>
        internal void Normalize()
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            RecentProjects = RecentProjects.FindAll(r => r != null && ProjectId.IsValid(r.ProjectId) && seen.Add(r.ProjectId));
            Trim();
        }

        private void Trim()
        {
            if (RecentProjects.Count > MaxRecentProjects)
            {
                RecentProjects.RemoveRange(MaxRecentProjects, RecentProjects.Count - MaxRecentProjects);
            }
        }
    }
}
