using System;
using System.Diagnostics;
using System.IO;

namespace IntunePackageBuilder.Analysis.Exe
{
    /// <summary>
    /// Reads the version resource of an EXE. The file is never started: only its version information
    /// is read (SPEC section 7.1).
    /// </summary>
    public static class ExeReader
    {
        private const int MinimumLength = 64;

        public static ExeMetadata Read(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("A path is required.", "path");
            }

            var fullPath = Path.GetFullPath(path);
            if (!File.Exists(fullPath))
            {
                throw new ExeReadException(fullPath, ExeReadProblem.FileNotFound);
            }

            if (!LooksLikeAnExecutable(fullPath))
            {
                throw new ExeReadException(fullPath, ExeReadProblem.NotAnExecutable);
            }

            var info = FileVersionInfo.GetVersionInfo(fullPath);
            return new ExeMetadata(
                Clean(info.ProductName),
                Clean(info.CompanyName),
                Clean(info.FileDescription),
                Clean(info.OriginalFilename),
                Clean(info.ProductVersion),
                NumericFileVersion(info));
        }

        /// <summary>Checks the DOS header magic ("MZ") and a minimum length.</summary>
        private static bool LooksLikeAnExecutable(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                if (stream.Length < MinimumLength)
                {
                    return false;
                }

                return stream.ReadByte() == 'M' && stream.ReadByte() == 'Z';
            }
        }

        private static string NumericFileVersion(FileVersionInfo info)
        {
            if (info.FileMajorPart == 0 && info.FileMinorPart == 0 && info.FileBuildPart == 0 && info.FilePrivatePart == 0)
            {
                return null;
            }

            return info.FileMajorPart + "." + info.FileMinorPart + "." + info.FileBuildPart + "." + info.FilePrivatePart;
        }

        private static string Clean(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            return value.Trim();
        }
    }
}
