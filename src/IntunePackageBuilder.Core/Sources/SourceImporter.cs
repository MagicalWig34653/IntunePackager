using System;
using System.Collections.Generic;
using System.IO;

namespace IntunePackageBuilder.Core.Sources
{
    public enum ImportProblem
    {
        NothingDropped,
        MultipleItems,
        NotFound,
        UnsupportedFileType,
        FolderNotAllowed,
        ReparsePointFound,
        TargetInsideSource,
        SourceInsideTarget,
        TargetNotEmpty,
        SourceAlreadyStored,
        InstallerOutsideSource,
        InstallerNotFound,
        PathTooLong
    }

    /// <summary>An import was refused before anything was copied. The UI maps <see cref="Problem"/> to a localized text.</summary>
    public sealed class ImportRejectedException : Exception
    {
        public ImportRejectedException(ImportProblem problem, string path)
            : base("Import refused: " + problem + (path != null ? " ('" + path + "')" : string.Empty))
        {
            Problem = problem;
            Path = path;
        }

        public ImportProblem Problem { get; private set; }

        /// <summary>The offending path, or null when the problem concerns the selection as a whole.</summary>
        public string Path { get; private set; }
    }

    public enum DroppedKind
    {
        Msi,
        Exe,
        Folder
    }

    /// <summary>What the user dropped or chose, after the selection was checked.</summary>
    public sealed class DroppedItem
    {
        public DroppedItem(DroppedKind kind, string path)
        {
            Kind = kind;
            Path = path;
        }

        public DroppedKind Kind { get; private set; }

        /// <summary>Full path of the file or folder.</summary>
        public string Path { get; private set; }
    }

    public sealed class ImportResult
    {
        public ImportResult(string installerRelativePath, SourceManifest manifest, long totalBytes)
        {
            InstallerRelativePath = installerRelativePath;
            Manifest = manifest;
            TotalBytes = totalBytes;
        }

        /// <summary>Installer path relative to the stored source folder, with <c>/</c> as separator.</summary>
        public string InstallerRelativePath { get; private set; }

        /// <summary>Manifest of the stored copy (not yet written to disk).</summary>
        public SourceManifest Manifest { get; private set; }

        public long TotalBytes { get; private set; }
    }

    /// <summary>
    /// Copies an installation source into a version's <c>source</c> folder (SPEC section 7.3).
    /// Everything is checked before the first byte is copied, so a refused import leaves no trace.
    /// The copy goes to a temporary sibling folder, is verified by checksum and then renamed into place,
    /// so a failed import never leaves a half-filled source folder. Originals are never changed or deleted.
    /// </summary>
    public static class SourceImporter
    {
        /// <summary>
        /// Longest full path the copy may create. Windows applications without long-path support fail beyond
        /// this length; the check turns that into an understandable refusal up front.
        /// </summary>
        public const int MaxPathLength = 259;

        /// <summary>
        /// Checks what the user dropped: exactly one MSI or EXE, or (only when allowed) one folder.
        /// </summary>
        public static DroppedItem Classify(IReadOnlyList<string> paths, bool allowFolders)
        {
            if (paths == null || paths.Count == 0)
            {
                throw new ImportRejectedException(ImportProblem.NothingDropped, null);
            }

            if (paths.Count > 1)
            {
                throw new ImportRejectedException(ImportProblem.MultipleItems, null);
            }

            var path = paths[0];
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ImportRejectedException(ImportProblem.NothingDropped, null);
            }

            var full = PathSafety.Full(path);
            if (Directory.Exists(full))
            {
                if (!allowFolders)
                {
                    throw new ImportRejectedException(ImportProblem.FolderNotAllowed, full);
                }

                return new DroppedItem(DroppedKind.Folder, full);
            }

            if (!File.Exists(full))
            {
                throw new ImportRejectedException(ImportProblem.NotFound, full);
            }

            return new DroppedItem(KindOfInstaller(full), full);
        }

        /// <summary>Stores a single MSI or EXE as the only file of the source folder.</summary>
        public static ImportResult ImportFile(string sourceFile, string targetDirectory)
        {
            var file = PathSafety.Full(sourceFile);
            if (!File.Exists(file))
            {
                throw new ImportRejectedException(ImportProblem.NotFound, file);
            }

            KindOfInstaller(file);
            if (PathSafety.IsReparsePoint(file))
            {
                throw new ImportRejectedException(ImportProblem.ReparsePointFound, file);
            }

            var target = FullTarget(targetDirectory);
            if (PathSafety.IsSameOrInside(target, file))
            {
                throw new ImportRejectedException(ImportProblem.SourceInsideTarget, file);
            }

            var root = Path.GetDirectoryName(file);
            return Execute(root, new List<string> { file }, Path.GetFileName(file), target);
        }

        /// <summary>Stores a whole vendor folder (with subfolders) and names the installer inside it.</summary>
        public static ImportResult ImportFolder(string sourceFolder, string installerRelativePath, string targetDirectory)
        {
            var root = PathSafety.Full(sourceFolder);
            if (!Directory.Exists(root))
            {
                throw new ImportRejectedException(ImportProblem.NotFound, root);
            }

            var target = FullTarget(targetDirectory);
            if (PathSafety.IsSameOrInside(root, target))
            {
                throw new ImportRejectedException(ImportProblem.TargetInsideSource, target);
            }

            if (PathSafety.IsSameOrInside(target, root))
            {
                throw new ImportRejectedException(ImportProblem.SourceInsideTarget, root);
            }

            var installer = ResolveInstaller(root, installerRelativePath);

            List<string> files;
            try
            {
                files = PathSafety.ListFiles(root);
            }
            catch (UnsafeSourceException ex)
            {
                throw new ImportRejectedException(ImportProblem.ReparsePointFound, ex.Path);
            }

            return Execute(root, files, installer, target);
        }

        private static ImportResult Execute(string root, List<string> files, string installerRelativePath, string target)
        {
            var staging = target + ".importing-" + Guid.NewGuid().ToString("N");

            foreach (var file in files)
            {
                var staged = staging + Path.DirectorySeparatorChar + PathSafety.Relative(root, file).Replace('/', Path.DirectorySeparatorChar);
                if (staged.Length > MaxPathLength)
                {
                    throw new ImportRejectedException(ImportProblem.PathTooLong, staged);
                }
            }

            if (Directory.Exists(target) && (Directory.GetFileSystemEntries(target).Length > 0))
            {
                throw new ImportRejectedException(ImportProblem.TargetNotEmpty, target);
            }

            try
            {
                Directory.CreateDirectory(staging);
                long total = 0;
                foreach (var file in files)
                {
                    var relative = PathSafety.Relative(root, file);
                    var destination = Path.Combine(staging, relative.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(destination));
                    File.Copy(file, destination, false);
                }

                var manifest = SourceManifestBuilder.Build(staging);
                foreach (var entry in manifest.Files)
                {
                    var original = SourceManifestBuilder.Describe(root, Path.Combine(root, entry.Path.Replace('/', Path.DirectorySeparatorChar)));
                    if (original.Size != entry.Size || !string.Equals(original.Sha256, entry.Sha256, StringComparison.Ordinal))
                    {
                        throw new IOException("The copy of '" + entry.Path + "' differs from the original.");
                    }

                    total += entry.Size;
                }

                if (Directory.Exists(target))
                {
                    Directory.Delete(target);
                }

                Directory.Move(staging, target);
                return new ImportResult(installerRelativePath, manifest, total);
            }
            catch
            {
                RemoveOwnStaging(staging);
                throw;
            }
        }

        private static string ResolveInstaller(string root, string installerRelativePath)
        {
            if (string.IsNullOrWhiteSpace(installerRelativePath))
            {
                throw new ImportRejectedException(ImportProblem.InstallerNotFound, null);
            }

            if (!Versions.ConfigurationValidator.IsSafeRelativePath(installerRelativePath))
            {
                throw new ImportRejectedException(ImportProblem.InstallerOutsideSource, installerRelativePath);
            }

            var native = installerRelativePath.Replace('/', Path.DirectorySeparatorChar);
            var full = PathSafety.Full(Path.Combine(root, native));
            if (!PathSafety.IsSameOrInside(root, full))
            {
                throw new ImportRejectedException(ImportProblem.InstallerOutsideSource, installerRelativePath);
            }

            if (!File.Exists(full))
            {
                throw new ImportRejectedException(ImportProblem.InstallerNotFound, installerRelativePath);
            }

            KindOfInstaller(full);
            return installerRelativePath.Replace('\\', '/');
        }

        /// <summary>Full target path; a target that is too long for Windows is refused with a clear reason.</summary>
        private static string FullTarget(string targetDirectory)
        {
            try
            {
                return PathSafety.Full(targetDirectory);
            }
            catch (PathTooLongException)
            {
                throw new ImportRejectedException(ImportProblem.PathTooLong, targetDirectory);
            }
        }

        private static DroppedKind KindOfInstaller(string file)
        {
            var extension = Path.GetExtension(file);
            if (string.Equals(extension, ".msi", StringComparison.OrdinalIgnoreCase))
            {
                return DroppedKind.Msi;
            }

            if (string.Equals(extension, ".exe", StringComparison.OrdinalIgnoreCase))
            {
                return DroppedKind.Exe;
            }

            throw new ImportRejectedException(ImportProblem.UnsupportedFileType, file);
        }

        /// <summary>Removes the temporary folder this import created itself (its name carries a unique id).</summary>
        private static void RemoveOwnStaging(string staging)
        {
            try
            {
                if (Directory.Exists(staging))
                {
                    Directory.Delete(staging, true);
                }
            }
            catch (IOException)
            {
                // The original error matters more than a failed cleanup.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
