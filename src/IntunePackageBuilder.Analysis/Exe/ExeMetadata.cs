using System;
using IntunePackageBuilder.Core.Versions;

namespace IntunePackageBuilder.Analysis.Exe
{
    public enum ExeReadProblem
    {
        FileNotFound,
        NotAnExecutable
    }

    /// <summary>An EXE could not be analyzed. The UI maps <see cref="Problem"/> to a localized text.</summary>
    public sealed class ExeReadException : Exception
    {
        public ExeReadException(string path, ExeReadProblem problem)
            : base("Cannot read EXE '" + path + "': " + problem)
        {
            Path = path;
            Problem = problem;
        }

        public string Path { get; private set; }

        public ExeReadProblem Problem { get; private set; }
    }

    /// <summary>
    /// File information of an EXE, read from its version resource only. Everything here is a suggestion:
    /// the version of a setup program can differ from the version of the file it installs, so the user
    /// must confirm the target version and pick the detection file (SPEC section 7.1).
    /// </summary>
    public sealed class ExeMetadata
    {
        public ExeMetadata(
            string productName,
            string companyName,
            string fileDescription,
            string originalFilename,
            string productVersion,
            string fileVersion)
        {
            ProductName = productName;
            CompanyName = companyName;
            FileDescription = fileDescription;
            OriginalFilename = originalFilename;
            ProductVersion = productVersion;
            FileVersion = fileVersion;
        }

        public string ProductName { get; private set; }

        public string CompanyName { get; private set; }

        public string FileDescription { get; private set; }

        public string OriginalFilename { get; private set; }

        /// <summary>The product version as text, exactly as stored (may contain non-numeric parts).</summary>
        public string ProductVersion { get; private set; }

        /// <summary>The numeric file version (up to four parts), or null when the file has none.</summary>
        public string FileVersion { get; private set; }

        /// <summary>True when the file carries any version information.</summary>
        public bool HasVersionInfo
        {
            get
            {
                return ProductName != null || CompanyName != null || FileDescription != null
                    || ProductVersion != null || FileVersion != null;
            }
        }

        public string SuggestedSoftwareName
        {
            get { return ProductName ?? FileDescription; }
        }

        public string SuggestedManufacturer
        {
            get { return CompanyName; }
        }

        /// <summary>
        /// A numeric version to offer as target version: the file version, else the product version when it
        /// is numeric. Trailing zero parts are dropped but at least two parts stay (<c>12.0.0.0</c> becomes <c>12.0</c>).
        /// Null when nothing numeric is known.
        /// </summary>
        public string SuggestedVersion
        {
            get
            {
                VersionNumber parsed;
                if (FileVersion != null && VersionNumber.TryParse(FileVersion, out parsed))
                {
                    return Shorten(parsed.Text);
                }

                if (ProductVersion != null && VersionNumber.TryParse(ProductVersion, out parsed))
                {
                    return Shorten(parsed.Text);
                }

                return null;
            }
        }

        private static string Shorten(string version)
        {
            var parts = new System.Collections.Generic.List<string>(version.Split('.'));
            while (parts.Count > 2 && parts[parts.Count - 1] == "0")
            {
                parts.RemoveAt(parts.Count - 1);
            }

            return string.Join(".", parts);
        }
    }
}
