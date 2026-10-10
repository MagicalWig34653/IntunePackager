using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using IntunePackageBuilder.Build.Packaging;

namespace IntunePackageBuilder.Build.Psadt
{
    /// <summary>
    /// Checks the toolkit ZIP the user supplied against the pinned versions and copies the module out of it. Only the
    /// <c>PSAppDeployToolkit</c> folder is used, and it is copied unchanged: the toolkit verifies its own data files and
    /// the license text (<c>COPYING.Lesser</c>) travels with the module.
    /// </summary>
    public static class PsadtPackage
    {
        public const string ModuleFolder = "PSAppDeployToolkit";
        public const long MaxUncompressedBytes = 400L * 1024 * 1024;
        public const int MaxEntries = 5000;

        private static readonly string[] RequiredEntries =
        {
            "PSAppDeployToolkit/PSAppDeployToolkit.psd1",
            "PSAppDeployToolkit/PSAppDeployToolkit.psm1",
            "PSAppDeployToolkit/COPYING.Lesser",
            "PSAppDeployToolkit/Config/config.psd1",
            "PSAppDeployToolkit/Strings/strings.psd1",
            "PSAppDeployToolkit/lib/PSADT.dll"
        };

        /// <exception cref="PsadtException">The ZIP is missing, unreadable, unknown (without confirmation), incomplete or unsafe.</exception>
        public static PsadtPackageInfo Inspect(string zipPath, string pinPath, bool allowUnknown)
        {
            if (string.IsNullOrWhiteSpace(zipPath) || !File.Exists(zipPath))
            {
                throw new PsadtException(PsadtProblem.PackageMissing, zipPath);
            }

            var pins = PsadtPins.Load(pinPath);
            string sha;
            try
            {
                sha = ContentPrepTool.ComputeSha256(zipPath);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                throw new PsadtException(PsadtProblem.PackageUnreadable, zipPath, exception);
            }

            var pinned = pins.FirstOrDefault(p => string.Equals(p.Sha256, sha, StringComparison.Ordinal));
            if (pinned == null && !allowUnknown)
            {
                throw new PsadtException(PsadtProblem.PackageNotRecognized, "SHA-256 " + sha);
            }

            try
            {
                using (var archive = ZipFile.OpenRead(zipPath))
                {
                    var module = ModuleEntries(archive).ToList();
                    if (module.Count > MaxEntries)
                    {
                        throw new PsadtException(PsadtProblem.PackageUnsafe, module.Count + " entries");
                    }

                    var names = new HashSet<string>(module.Select(e => Normalize(e.FullName)), StringComparer.OrdinalIgnoreCase);
                    foreach (var required in RequiredEntries)
                    {
                        if (!names.Contains(required))
                        {
                            throw new PsadtException(PsadtProblem.PackageInvalid, "missing " + required);
                        }
                    }

                    var total = module.Sum(e => e.Length);
                    if (total > MaxUncompressedBytes)
                    {
                        throw new PsadtException(PsadtProblem.PackageUnsafe, total + " bytes");
                    }

                    return new PsadtPackageInfo { Version = pinned == null ? null : pinned.Version, Sha256 = sha, UncompressedBytes = total };
                }
            }
            catch (InvalidDataException exception)
            {
                throw new PsadtException(PsadtProblem.PackageUnreadable, zipPath, exception);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                throw new PsadtException(PsadtProblem.PackageUnreadable, zipPath, exception);
            }
        }

        /// <summary>Copies the module folder into <paramref name="packageRoot"/> as <c>PSAppDeployToolkit</c>. <paramref name="checkPath"/> may refuse a path that is too long.</summary>
        public static void ExtractModule(string zipPath, string packageRoot, Action<string> checkPath)
        {
            var root = Path.GetFullPath(packageRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            using (var archive = ZipFile.OpenRead(zipPath))
            {
                foreach (var entry in ModuleEntries(archive))
                {
                    var relative = Normalize(entry.FullName).Replace('/', Path.DirectorySeparatorChar);
                    var target = Path.GetFullPath(Path.Combine(root, relative));
                    if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new PsadtException(PsadtProblem.PackageUnsafe, entry.FullName);
                    }

                    if (checkPath != null)
                    {
                        checkPath(target);
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    using (var input = entry.Open())
                    using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        input.CopyTo(output);
                    }
                }
            }
        }

        /// <summary>Files of the module that belong into a package: no folders, no old-style front end, no debug symbols.</summary>
        private static IEnumerable<ZipArchiveEntry> ModuleEntries(ZipArchive archive)
        {
            foreach (var entry in archive.Entries)
            {
                var name = Normalize(entry.FullName);
                if (!name.StartsWith(ModuleFolder + "/", StringComparison.OrdinalIgnoreCase) || name.EndsWith("/", StringComparison.Ordinal))
                {
                    continue;
                }

                if (name.StartsWith(ModuleFolder + "/Frontend/", StringComparison.OrdinalIgnoreCase)
                    || name.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!IsSafe(name))
                {
                    throw new PsadtException(PsadtProblem.PackageUnsafe, entry.FullName);
                }

                yield return entry;
            }
        }

        private static string Normalize(string name)
        {
            return name.Replace('\\', '/');
        }

        private static bool IsSafe(string name)
        {
            if (name.IndexOf(':') >= 0 || name.StartsWith("/", StringComparison.Ordinal))
            {
                return false;
            }

            return !name.Split('/').Any(segment => segment == ".." || segment == ".");
        }
    }
}
