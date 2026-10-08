using System;
using System.Collections.Generic;

namespace IntunePackageBuilder.Analysis.Msi
{
    public enum MsiReadProblem
    {
        FileNotFound,
        NotAnInstallerDatabase,
        MissingProductCode,
        InvalidProductCode,
        MissingProductVersion,
        ReadFailed
    }

    /// <summary>An MSI could not be analyzed. The UI maps <see cref="Problem"/> to a localized text.</summary>
    public sealed class MsiReadException : Exception
    {
        public MsiReadException(string path, MsiReadProblem problem, uint nativeError)
            : base("Cannot read MSI '" + path + "': " + problem + (nativeError != 0 ? " (Windows Installer error " + nativeError + ")" : string.Empty))
        {
            Path = path;
            Problem = problem;
            NativeError = nativeError;
        }

        public string Path { get; private set; }

        public MsiReadProblem Problem { get; private set; }

        /// <summary>The Windows Installer error code, or 0 when the problem was found without a native call.</summary>
        public uint NativeError { get; private set; }
    }

    /// <summary>Values read from the <c>Property</c> table of an MSI database, plus source requirements.</summary>
    public sealed class MsiMetadata
    {
        public MsiMetadata(
            IDictionary<string, string> properties,
            IList<string> externalCabinets,
            bool requiresSourceFolder)
        {
            Properties = new Dictionary<string, string>(properties, StringComparer.Ordinal);
            ExternalCabinets = new List<string>(externalCabinets).AsReadOnly();
            RequiresSourceFolder = requiresSourceFolder;
        }

        /// <summary>All entries of the Property table (for the advanced metadata view).</summary>
        public IReadOnlyDictionary<string, string> Properties { get; private set; }

        public string ProductName
        {
            get { return Get("ProductName"); }
        }

        public string Manufacturer
        {
            get { return Get("Manufacturer"); }
        }

        public string ProductVersion
        {
            get { return Get("ProductVersion"); }
        }

        /// <summary>The product code in braces, as stored in the MSI.</summary>
        public string ProductCode
        {
            get { return Get("ProductCode"); }
        }

        public string UpgradeCode
        {
            get { return Get("UpgradeCode"); }
        }

        /// <summary>Value of ALLUSERS, or null when the MSI does not set it (then it installs per user).</summary>
        public string AllUsers
        {
            get { return Get("ALLUSERS"); }
        }

        /// <summary>Cabinet files that live next to the MSI instead of inside it.</summary>
        public IReadOnlyList<string> ExternalCabinets { get; private set; }

        /// <summary>
        /// True when the MSI needs files from its source folder: external cabinets, or files that are
        /// not compressed into a cabinet at all. A single MSI file is then not enough; the whole vendor
        /// folder must be imported. This is derived from the Media and File tables.
        /// </summary>
        public bool RequiresSourceFolder { get; private set; }

        private string Get(string name)
        {
            string value;
            return Properties.TryGetValue(name, out value) ? value : null;
        }
    }
}
