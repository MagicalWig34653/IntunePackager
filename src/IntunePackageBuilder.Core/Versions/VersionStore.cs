using System;
using System.Collections.Generic;
using System.IO;
using IntunePackageBuilder.Core.Projects;
using IntunePackageBuilder.Core.Sources;
using IntunePackageBuilder.Core.Storage;

namespace IntunePackageBuilder.Core.Versions
{
    /// <summary>One entry of a project's version list. A broken version is reported, never thrown.</summary>
    public sealed class VersionListEntry
    {
        public string FolderName { get; set; }

        /// <summary>Parsed from the folder name; null when the folder name is not a version.</summary>
        public VersionNumber Version { get; set; }

        /// <summary>The loaded configuration, or null when <see cref="Error"/> is set.</summary>
        public PackageVersionConfig Config { get; set; }

        public Exception Error { get; set; }
    }

    /// <summary>
    /// Stores the configuration of each software version below <c>&lt;project&gt;/versions/&lt;version&gt;</c>.
    /// Writes take the per-version lock, so two programs never write the same version at once.
    /// The target version identifies the folder; changing it means creating a new version.
    /// </summary>
    public sealed class VersionStore
    {
        public const string VersionsFolder = "versions";

        private readonly ProjectStore _projects;
        private readonly int _currentSchemaVersion;
        private readonly IReadOnlyList<IMigrationStep> _steps;

        public VersionStore(ProjectStore projects)
            : this(projects, PackageVersionConfig.CurrentSchemaVersion, new IMigrationStep[0])
        {
        }

        internal VersionStore(ProjectStore projects, int currentSchemaVersion, IReadOnlyList<IMigrationStep> steps)
        {
            if (projects == null)
            {
                throw new ArgumentNullException("projects");
            }

            _projects = projects;
            _currentSchemaVersion = currentSchemaVersion;
            _steps = steps;
        }

        /// <summary>Directory of a version, resolved by numeric equality (<c>1.0</c> finds <c>1.0.0</c>).</summary>
        /// <exception cref="VersionNotFoundException">No such version exists.</exception>
        public string VersionDirectory(string projectId, string version)
        {
            var number = ParseVersion(version);
            var folder = FindFolder(projectId, number);
            if (folder == null)
            {
                throw new VersionNotFoundException(projectId, version);
            }

            return Path.Combine(VersionsDirectory(projectId), folder);
        }

        /// <summary>Folder that holds the stored installation source of a version (<c>source</c>).</summary>
        public string SourceDirectory(string projectId, string version)
        {
            return Path.Combine(VersionDirectory(projectId, version), "source");
        }

        /// <summary>Path of the source manifest of a version (<c>source-manifest.json</c>).</summary>
        public string SourceManifestPath(string projectId, string version)
        {
            return Path.Combine(VersionDirectory(projectId, version), SourceManifest.FileName);
        }

        /// <summary>
        /// Stores the installation source of a version: copies it into <c>source</c> and writes
        /// <c>source-manifest.json</c>. Takes the version lock. A version keeps one stored source for good;
        /// a changed installer needs a new version (SPEC section 6.4).
        /// </summary>
        /// <exception cref="ImportRejectedException">The selection or the source is not acceptable; nothing was copied.</exception>
        public ImportResult ImportSource(string projectId, string version, DroppedItem item, string installerRelativePath)
        {
            if (item == null)
            {
                throw new ArgumentNullException("item");
            }

            var directory = VersionDirectory(projectId, version);
            using (VersionLock.Acquire(directory))
            {
                var manifestPath = Path.Combine(directory, SourceManifest.FileName);
                if (File.Exists(manifestPath))
                {
                    throw new ImportRejectedException(ImportProblem.SourceAlreadyStored, manifestPath);
                }

                var target = Path.Combine(directory, "source");
                var result = item.Kind == DroppedKind.Folder
                    ? SourceImporter.ImportFolder(item.Path, installerRelativePath, target)
                    : SourceImporter.ImportFile(item.Path, target);
                try
                {
                    SourceManifestStore.WriteNew(manifestPath, result.Manifest);
                }
                catch
                {
                    // Do not leave a stored source without its manifest: remove the copy this call just made.
                    if (Directory.Exists(target))
                    {
                        Directory.Delete(target, true);
                    }

                    throw;
                }

                return result;
            }
        }

        /// <summary>Creates a new version from a configuration. The configuration may be incomplete (a draft).</summary>
        public PackageVersionConfig Create(string projectId, PackageVersionConfig config)
        {
            if (config == null)
            {
                throw new ArgumentNullException("config");
            }

            _projects.Load(projectId);
            RequireProject(projectId, config);
            var number = ParseVersion(config.Identity.TargetVersion);

            if (FindFolder(projectId, number) != null)
            {
                throw new VersionExistsException(projectId, number.Text);
            }

            var directory = Path.Combine(VersionsDirectory(projectId), number.Text);
            using (VersionLock.Acquire(directory))
            {
                var file = Path.Combine(directory, PackageVersionConfig.FileName);
                if (File.Exists(file))
                {
                    throw new VersionExistsException(projectId, number.Text);
                }

                config.SchemaVersion = _currentSchemaVersion;
                VersionedJsonFile.Write(file, config);
            }

            return config;
        }

        public PackageVersionConfig Load(string projectId, string version)
        {
            var number = ParseVersion(version);
            var folder = FindFolder(projectId, number);
            if (folder == null)
            {
                throw new VersionNotFoundException(projectId, version);
            }

            return ReadFolder(projectId, folder, number);
        }

        /// <summary>
        /// Saves changes to an existing version. The configuration must still describe the same target
        /// version; use <see cref="Create"/> for a new one. Refuses files written by a newer program.
        /// </summary>
        /// <exception cref="LockHeldException">Another process is writing this version.</exception>
        public void Save(string projectId, string version, PackageVersionConfig config)
        {
            if (config == null)
            {
                throw new ArgumentNullException("config");
            }

            RequireProject(projectId, config);
            var number = ParseVersion(version);
            VersionNumber configured;
            if (!VersionNumber.TryParse(config.Identity.TargetVersion, out configured) || !configured.Equals(number))
            {
                throw new ArgumentException("The target version of the configuration must match the stored version '" + version + "'.", "config");
            }

            var folder = FindFolder(projectId, number);
            if (folder == null)
            {
                throw new VersionNotFoundException(projectId, version);
            }

            var directory = Path.Combine(VersionsDirectory(projectId), folder);
            using (VersionLock.Acquire(directory))
            {
                var file = Path.Combine(directory, PackageVersionConfig.FileName);
                VersionedJsonFile.GuardAgainstNewerSchema(file, _currentSchemaVersion);
                config.SchemaVersion = _currentSchemaVersion;
                VersionedJsonFile.Write(file, config);
            }
        }

        /// <summary>
        /// Lists the versions of a project, newest first by numeric version. Folders that are not versions
        /// come last. A broken version never hides or damages the others.
        /// </summary>
        public IReadOnlyList<VersionListEntry> List(string projectId)
        {
            var entries = new List<VersionListEntry>();
            var root = VersionsDirectory(projectId);
            if (!Directory.Exists(root))
            {
                return entries;
            }

            foreach (var directory in Directory.GetDirectories(root))
            {
                var name = Path.GetFileName(directory);
                if (!File.Exists(Path.Combine(directory, PackageVersionConfig.FileName)))
                {
                    continue;
                }

                var entry = new VersionListEntry { FolderName = name };
                VersionNumber number;
                if (VersionNumber.TryParse(name, out number) && number.Text == name)
                {
                    entry.Version = number;
                }

                try
                {
                    if (entry.Version == null)
                    {
                        throw new StorageFormatException(Path.Combine(directory, PackageVersionConfig.FileName), "folder name '" + name + "' is not a version");
                    }

                    entry.Config = ReadFolder(projectId, name, entry.Version);
                }
                catch (Exception ex) when (IsReadFailure(ex))
                {
                    entry.Error = ex;
                }

                entries.Add(entry);
            }

            entries.Sort(CompareNewestFirst);
            return entries;
        }

        private PackageVersionConfig ReadFolder(string projectId, string folder, VersionNumber number)
        {
            var file = Path.Combine(VersionsDirectory(projectId), folder, PackageVersionConfig.FileName);
            var config = VersionedJsonFile.Read<PackageVersionConfig>(file, _currentSchemaVersion, _steps, loaded =>
            {
                if (!string.Equals(loaded.ProjectId, projectId, StringComparison.Ordinal))
                {
                    throw new StorageFormatException(file, "project ID '" + loaded.ProjectId + "' does not match project '" + projectId + "'");
                }

                VersionNumber stored;
                if (!VersionNumber.TryParse(loaded.Identity.TargetVersion, out stored) || !stored.Equals(number))
                {
                    throw new StorageFormatException(file, "target version '" + loaded.Identity.TargetVersion + "' does not match folder '" + folder + "'");
                }
            });
            return config;
        }

        private string VersionsDirectory(string projectId)
        {
            return Path.Combine(_projects.ProjectDirectory(projectId), VersionsFolder);
        }

        /// <summary>Returns the name of the folder whose version equals <paramref name="number"/>, or null.</summary>
        private string FindFolder(string projectId, VersionNumber number)
        {
            var root = VersionsDirectory(projectId);
            if (!Directory.Exists(root))
            {
                return null;
            }

            foreach (var directory in Directory.GetDirectories(root))
            {
                var name = Path.GetFileName(directory);
                VersionNumber existing;
                if (VersionNumber.TryParse(name, out existing) && existing.Equals(number))
                {
                    return name;
                }
            }

            return null;
        }

        private static VersionNumber ParseVersion(string version)
        {
            VersionNumber number;
            if (!VersionNumber.TryParse(version, out number))
            {
                throw new ArgumentException("'" + version + "' is not a numeric version.", "version");
            }

            return number;
        }

        private static void RequireProject(string projectId, PackageVersionConfig config)
        {
            if (!string.Equals(config.ProjectId, projectId, StringComparison.Ordinal))
            {
                throw new ArgumentException("The configuration belongs to project '" + config.ProjectId + "', not '" + projectId + "'.", "config");
            }
        }

        private static int CompareNewestFirst(VersionListEntry left, VersionListEntry right)
        {
            if (left.Version != null && right.Version != null)
            {
                var byVersion = right.Version.CompareTo(left.Version);
                return byVersion != 0 ? byVersion : string.CompareOrdinal(left.FolderName, right.FolderName);
            }

            if (left.Version != null)
            {
                return -1;
            }

            if (right.Version != null)
            {
                return 1;
            }

            return string.CompareOrdinal(left.FolderName, right.FolderName);
        }

        private static bool IsReadFailure(Exception ex)
        {
            return ex is StorageFormatException
                || ex is UnsupportedSchemaException
                || ex is MigrationException
                || ex is IOException
                || ex is UnauthorizedAccessException;
        }
    }
}
