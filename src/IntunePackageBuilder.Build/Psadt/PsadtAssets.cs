using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using IntunePackageBuilder.Core.Versions;

namespace IntunePackageBuilder.Build.Psadt
{
    public enum PsadtImageKind
    {
        Logo,
        LogoDark,
        Banner
    }

    /// <summary>
    /// Branding images of the toolkit dialogs. They are stored in the folder <c>psadt-assets</c> of the version so a
    /// rebuild does not depend on files somewhere on the disk of the user; the configuration only holds the file names.
    /// </summary>
    public static class PsadtAssets
    {
        public const string FolderName = "psadt-assets";
        public const long MaxBytes = 2L * 1024 * 1024;

        public static string AssetsDirectory(string versionDirectory)
        {
            return Path.Combine(versionDirectory, FolderName);
        }

        /// <summary>
        /// Checks and copies an image into the version folder and returns the file name to store in the configuration.
        /// An existing image of the same kind is replaced; the file name is derived from the kind, never from the source.
        /// </summary>
        /// <exception cref="PsadtException">The file is missing, too large, or not a PNG or JPEG image.</exception>
        public static string Import(string versionDirectory, PsadtImageKind kind, string sourcePath)
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            {
                throw new PsadtException(PsadtProblem.ImageInvalid, sourcePath);
            }

            var info = new FileInfo(sourcePath);
            if (info.Length == 0 || info.Length > MaxBytes)
            {
                throw new PsadtException(PsadtProblem.ImageInvalid, sourcePath + " (" + info.Length + " bytes)");
            }

            var extension = ImageExtension(sourcePath);
            if (extension == null)
            {
                throw new PsadtException(PsadtProblem.ImageInvalid, sourcePath);
            }

            var name = FileNameFor(kind) + extension;
            var folder = AssetsDirectory(versionDirectory);
            System.IO.Directory.CreateDirectory(folder);
            var target = Path.Combine(folder, name);
            var temporary = target + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                File.Copy(sourcePath, temporary, false);
                DeleteSibling(folder, FileNameFor(kind), name);
                if (File.Exists(target))
                {
                    File.Delete(target);
                }

                File.Move(temporary, target);
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }

            return name;
        }

        /// <summary>Copies the images of one version to another (a new version based on a template keeps the branding). Never overwrites.</summary>
        public static void CopyAll(string fromVersionDirectory, string toVersionDirectory)
        {
            var from = AssetsDirectory(fromVersionDirectory);
            if (!System.IO.Directory.Exists(from))
            {
                return;
            }

            var to = AssetsDirectory(toVersionDirectory);
            System.IO.Directory.CreateDirectory(to);
            foreach (var file in System.IO.Directory.GetFiles(from))
            {
                var target = Path.Combine(to, Path.GetFileName(file));
                if (!File.Exists(target))
                {
                    File.Copy(file, target, false);
                }
            }
        }

        /// <summary>Names of the images the configuration refers to that are not in the version folder.</summary>
        public static IReadOnlyList<string> Missing(string versionDirectory, PsadtSection options)
        {
            var missing = new List<string>();
            foreach (var name in Referenced(options))
            {
                if (!File.Exists(Path.Combine(AssetsDirectory(versionDirectory), name)))
                {
                    missing.Add(name);
                }
            }

            return missing;
        }

        public static IReadOnlyList<string> Referenced(PsadtSection options)
        {
            return new[] { options.LogoFile, options.LogoDarkFile, options.BannerFile }
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary><c>.png</c> or <c>.jpg</c> when the first bytes say so, otherwise null. The extension of the source is not trusted.</summary>
        public static string ImageExtension(string path)
        {
            var header = new byte[8];
            int read;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                read = stream.Read(header, 0, header.Length);
            }

            if (read >= 8 && header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47
                && header[4] == 0x0D && header[5] == 0x0A && header[6] == 0x1A && header[7] == 0x0A)
            {
                return ".png";
            }

            if (read >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
            {
                return ".jpg";
            }

            return null;
        }

        private static string FileNameFor(PsadtImageKind kind)
        {
            switch (kind)
            {
                case PsadtImageKind.Logo:
                    return "logo";
                case PsadtImageKind.LogoDark:
                    return "logo-dark";
                default:
                    return "banner";
            }
        }

        private static void DeleteSibling(string folder, string stem, string keepName)
        {
            foreach (var extension in new[] { ".png", ".jpg" })
            {
                var other = stem + extension;
                if (!string.Equals(other, keepName, StringComparison.Ordinal))
                {
                    var path = Path.Combine(folder, other);
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                    }
                }
            }
        }
    }
}
