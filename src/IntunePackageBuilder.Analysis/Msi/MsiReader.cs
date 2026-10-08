using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using IntunePackageBuilder.Core.Versions;

namespace IntunePackageBuilder.Analysis.Msi
{
    /// <summary>
    /// Reads metadata from an MSI database without installing it and without running custom actions
    /// (SPEC section 7.1). The database is opened read-only and only queried.
    /// </summary>
    public static class MsiReader
    {
        public static MsiMetadata Read(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("A path is required.", "path");
            }

            var fullPath = Path.GetFullPath(path);
            if (!File.Exists(fullPath))
            {
                throw new MsiReadException(fullPath, MsiReadProblem.FileNotFound, 0);
            }

            // An MSI is an OLE compound file. Checking the signature first recognizes foreign files
            // reliably instead of depending on which error code the installer API returns for them.
            if (!HasCompoundFileSignature(fullPath))
            {
                throw new MsiReadException(fullPath, MsiReadProblem.NotAnInstallerDatabase, 0);
            }

            IntPtr database;
            var result = NativeMsi.MsiOpenDatabase(fullPath, NativeMsi.OpenReadOnly, out database);
            if (result != NativeMsi.ErrorSuccess)
            {
                var problem = result == NativeMsi.ErrorInstallPackageInvalid || result == NativeMsi.ErrorInstallPackageOpenFailed
                    ? MsiReadProblem.NotAnInstallerDatabase
                    : MsiReadProblem.ReadFailed;
                throw new MsiReadException(fullPath, problem, result);
            }

            using (new MsiHandle(database))
            {
                var properties = ReadProperties(fullPath, database);
                RequireIdentity(fullPath, properties);

                var cabinets = new List<string>();
                var anyCabinet = ReadCabinets(fullPath, database, cabinets);
                var hasFiles = HasRows(fullPath, database, "SELECT `File` FROM `File`");
                var requiresSource = cabinets.Count > 0 || (hasFiles && !anyCabinet);

                return new MsiMetadata(properties, cabinets, requiresSource);
            }
        }

        private static readonly byte[] CompoundFileSignature = { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 };

        private static bool HasCompoundFileSignature(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                var header = new byte[CompoundFileSignature.Length];
                var read = 0;
                while (read < header.Length)
                {
                    var count = stream.Read(header, read, header.Length - read);
                    if (count == 0)
                    {
                        return false;
                    }

                    read += count;
                }

                for (var i = 0; i < header.Length; i++)
                {
                    if (header[i] != CompoundFileSignature[i])
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        private static void RequireIdentity(string path, IDictionary<string, string> properties)
        {
            string productCode;
            if (!properties.TryGetValue("ProductCode", out productCode) || string.IsNullOrWhiteSpace(productCode))
            {
                throw new MsiReadException(path, MsiReadProblem.MissingProductCode, 0);
            }

            if (!ConfigurationValidator.IsProductCode(productCode.Trim()))
            {
                throw new MsiReadException(path, MsiReadProblem.InvalidProductCode, 0);
            }

            string version;
            if (!properties.TryGetValue("ProductVersion", out version) || string.IsNullOrWhiteSpace(version))
            {
                throw new MsiReadException(path, MsiReadProblem.MissingProductVersion, 0);
            }

            properties["ProductCode"] = productCode.Trim();
            properties["ProductVersion"] = version.Trim();
        }

        private static Dictionary<string, string> ReadProperties(string path, IntPtr database)
        {
            var properties = new Dictionary<string, string>(StringComparer.Ordinal);
            ForEachRecord(path, database, "SELECT `Property`, `Value` FROM `Property`", record =>
            {
                properties[GetString(path, record, 1)] = GetString(path, record, 2);
                return true;
            });
            return properties;
        }

        /// <summary>Collects cabinets stored next to the MSI. Returns true when any cabinet (embedded or external) exists.</summary>
        private static bool ReadCabinets(string path, IntPtr database, List<string> external)
        {
            var any = false;
            ForEachRecord(path, database, "SELECT `Cabinet` FROM `Media`", record =>
            {
                var cabinet = GetString(path, record, 1);
                if (cabinet.Length > 0)
                {
                    any = true;
                    if (!cabinet.StartsWith("#", StringComparison.Ordinal) && !external.Contains(cabinet))
                    {
                        external.Add(cabinet);
                    }
                }

                return true;
            });
            return any;
        }

        private static bool HasRows(string path, IntPtr database, string query)
        {
            var found = false;
            ForEachRecord(path, database, query, record =>
            {
                found = true;
                return false;
            });
            return found;
        }

        /// <summary>
        /// Runs a query and calls <paramref name="visit"/> per row until it returns false.
        /// A table that does not exist counts as having no rows.
        /// </summary>
        private static void ForEachRecord(string path, IntPtr database, string query, Func<IntPtr, bool> visit)
        {
            IntPtr viewHandle;
            var result = NativeMsi.MsiDatabaseOpenView(database, query, out viewHandle);
            if (result == NativeMsi.ErrorBadQuerySyntax)
            {
                return;
            }

            if (result != NativeMsi.ErrorSuccess)
            {
                throw new MsiReadException(path, MsiReadProblem.ReadFailed, result);
            }

            using (new MsiHandle(viewHandle))
            {
                result = NativeMsi.MsiViewExecute(viewHandle, IntPtr.Zero);
                if (result != NativeMsi.ErrorSuccess)
                {
                    throw new MsiReadException(path, MsiReadProblem.ReadFailed, result);
                }

                try
                {
                    while (true)
                    {
                        IntPtr recordHandle;
                        result = NativeMsi.MsiViewFetch(viewHandle, out recordHandle);
                        if (result == NativeMsi.ErrorNoMoreItems)
                        {
                            break;
                        }

                        if (result != NativeMsi.ErrorSuccess)
                        {
                            throw new MsiReadException(path, MsiReadProblem.ReadFailed, result);
                        }

                        using (new MsiHandle(recordHandle))
                        {
                            if (!visit(recordHandle))
                            {
                                break;
                            }
                        }
                    }
                }
                finally
                {
                    NativeMsi.MsiViewClose(viewHandle);
                }
            }
        }

        private static string GetString(string path, IntPtr record, uint field)
        {
            uint length = 256;
            var buffer = new StringBuilder((int)length);
            var result = NativeMsi.MsiRecordGetString(record, field, buffer, ref length);
            if (result == NativeMsi.ErrorMoreData)
            {
                length++;
                buffer = new StringBuilder((int)length);
                result = NativeMsi.MsiRecordGetString(record, field, buffer, ref length);
            }

            if (result != NativeMsi.ErrorSuccess)
            {
                throw new MsiReadException(path, MsiReadProblem.ReadFailed, result);
            }

            return buffer.ToString();
        }
    }
}
