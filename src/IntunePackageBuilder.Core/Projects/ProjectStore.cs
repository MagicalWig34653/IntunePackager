using System;
using System.Collections.Generic;
using System.IO;
using IntunePackageBuilder.Core.Storage;

namespace IntunePackageBuilder.Core.Projects
{
    /// <summary>One entry of the project list. A broken project is reported, never thrown.</summary>
    public sealed class ProjectListEntry
    {
        public string FolderName { get; set; }

        /// <summary>The loaded project, or null when <see cref="Error"/> is set.</summary>
        public Project Project { get; set; }

        public Exception Error { get; set; }
    }

    /// <summary>Reads and writes projects below a base folder. Holds no state between calls.</summary>
    public sealed class ProjectStore
    {
        private readonly string _baseFolder;
        private readonly int _currentSchemaVersion;
        private readonly IReadOnlyList<IMigrationStep> _steps;

        public ProjectStore(string baseFolder)
            : this(baseFolder, Project.CurrentSchemaVersion, new IMigrationStep[0])
        {
        }

        internal ProjectStore(string baseFolder, int currentSchemaVersion, IReadOnlyList<IMigrationStep> steps)
        {
            if (string.IsNullOrWhiteSpace(baseFolder))
            {
                throw new ArgumentException("A base folder is required.", "baseFolder");
            }

            _baseFolder = baseFolder;
            _currentSchemaVersion = currentSchemaVersion;
            _steps = steps;
        }

        public string BaseFolder
        {
            get { return _baseFolder; }
        }

        public string ProjectDirectory(string projectId)
        {
            RequireValidId(projectId);
            return Path.Combine(_baseFolder, projectId);
        }

        public string ProjectFile(string projectId)
        {
            return Path.Combine(ProjectDirectory(projectId), Project.FileName);
        }

        public Project Create(string projectId, string displayName)
        {
            RequireValidId(projectId);
            if (string.IsNullOrWhiteSpace(displayName))
            {
                throw new ArgumentException("A display name is required.", "displayName");
            }

            var file = ProjectFile(projectId);
            if (File.Exists(file))
            {
                throw new ProjectExistsException(projectId);
            }

            var project = new Project
            {
                SchemaVersion = _currentSchemaVersion,
                ProjectId = projectId,
                DisplayName = displayName.Trim(),
                CreatedUtc = DateTime.UtcNow,
                Notes = string.Empty
            };

            VersionedJsonFile.Write(file, project);
            return project;
        }

        public Project Load(string projectId)
        {
            var file = ProjectFile(projectId);
            if (!File.Exists(file))
            {
                throw new ProjectNotFoundException(projectId);
            }

            var project = Read(file);
            if (!string.Equals(project.ProjectId, projectId, StringComparison.Ordinal))
            {
                throw new StorageFormatException(file, "project ID '" + project.ProjectId + "' does not match folder '" + projectId + "'");
            }

            return project;
        }

        /// <summary>Saves a project. Refuses to overwrite a file written by a newer program version.</summary>
        public void Save(Project project)
        {
            if (project == null)
            {
                throw new ArgumentNullException("project");
            }

            var file = ProjectFile(project.ProjectId);
            if (File.Exists(file))
            {
                VersionedJsonFile.GuardAgainstNewerSchema(file, _currentSchemaVersion);
            }

            project.SchemaVersion = _currentSchemaVersion;
            VersionedJsonFile.Write(file, project);
        }

        public void SaveNotes(string projectId, string notes)
        {
            var project = Load(projectId);
            project.Notes = notes ?? string.Empty;
            Save(project);
        }

        /// <summary>Lists all project folders. A broken project never hides or damages the others.</summary>
        public IReadOnlyList<ProjectListEntry> List()
        {
            var entries = new List<ProjectListEntry>();
            if (!Directory.Exists(_baseFolder))
            {
                return entries;
            }

            var directories = Directory.GetDirectories(_baseFolder);
            Array.Sort(directories, StringComparer.OrdinalIgnoreCase);
            foreach (var directory in directories)
            {
                var name = Path.GetFileName(directory);
                if (!File.Exists(Path.Combine(directory, Project.FileName)))
                {
                    continue;
                }

                var entry = new ProjectListEntry { FolderName = name };
                try
                {
                    entry.Project = Load(name);
                }
                catch (Exception ex) when (IsReadFailure(ex))
                {
                    entry.Error = ex;
                }

                entries.Add(entry);
            }

            return entries;
        }

        private Project Read(string file)
        {
            return VersionedJsonFile.Read<Project>(file, _currentSchemaVersion, _steps, project =>
            {
                if (string.IsNullOrEmpty(project.ProjectId))
                {
                    throw new StorageFormatException(file, "missing project ID");
                }
            });
        }

        private static void RequireValidId(string projectId)
        {
            var problem = ProjectId.Check(projectId);
            if (problem != ProjectIdProblem.None)
            {
                throw new InvalidProjectIdException(projectId, problem);
            }
        }

        private static bool IsReadFailure(Exception ex)
        {
            return ex is StorageFormatException
                || ex is UnsupportedSchemaException
                || ex is MigrationException
                || ex is InvalidProjectIdException
                || ex is IOException
                || ex is UnauthorizedAccessException;
        }
    }
}
