using System;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace IntunePackageBuilder.Build.Packaging
{
    /// <summary>
    /// Checks the structure of a <c>.intunewin</c> file (SPEC section 7.2, step 8): a ZIP container with the encrypted
    /// content and the detection metadata that Intune reads. The content itself is encrypted and cannot be checked.
    /// </summary>
    public static class IntunewinVerifier
    {
        public const string ContentEntry = "IntuneWinPackage/Contents/IntunePackage.intunewin";
        public const string MetadataEntry = "IntuneWinPackage/Metadata/Detection.xml";

        /// <exception cref="ContentPrepException">The file is missing, empty or not a <c>.intunewin</c> container.</exception>
        public static void Verify(string path)
        {
            if (!File.Exists(path))
            {
                throw new ContentPrepException(ContentPrepProblem.OutputMissing, path);
            }

            if (new FileInfo(path).Length == 0)
            {
                throw new ContentPrepException(ContentPrepProblem.OutputInvalid, "empty file: " + path);
            }

            try
            {
                using (var archive = ZipFile.OpenRead(path))
                {
                    RequireEntry(archive, ContentEntry, path);
                    RequireEntry(archive, MetadataEntry, path);
                }
            }
            catch (InvalidDataException exception)
            {
                throw new ContentPrepException(ContentPrepProblem.OutputInvalid, "not a ZIP container: " + path + " (" + exception.Message + ")");
            }
        }

        private static void RequireEntry(ZipArchive archive, string name, string path)
        {
            var entry = archive.Entries.FirstOrDefault(e => string.Equals(e.FullName.Replace('\\', '/'), name, StringComparison.OrdinalIgnoreCase));
            if (entry == null)
            {
                throw new ContentPrepException(ContentPrepProblem.OutputInvalid, "entry '" + name + "' is missing in " + path);
            }

            if (entry.Length == 0)
            {
                throw new ContentPrepException(ContentPrepProblem.OutputInvalid, "entry '" + name + "' is empty in " + path);
            }
        }
    }
}
