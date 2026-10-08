using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace IntunePackageBuilder.Analysis.Tests
{
    /// <summary>
    /// Builds small, controlled MSI databases for tests. Only database tables are written; the result
    /// cannot install anything. No vendor installer is ever used (SPEC section 12.1).
    /// </summary>
    internal sealed class TestMsi
    {
        private readonly Dictionary<string, string> _properties = new Dictionary<string, string>(StringComparer.Ordinal);
        private List<string> _mediaCabinets;
        private int _fileCount;
        private bool _withCustomAction;

        public static TestMsi WithIdentity(string productCode, string productVersion)
        {
            var msi = new TestMsi();
            if (productCode != null)
            {
                msi._properties["ProductCode"] = productCode;
            }

            if (productVersion != null)
            {
                msi._properties["ProductVersion"] = productVersion;
            }

            return msi;
        }

        public TestMsi Property(string name, string value)
        {
            _properties[name] = value;
            return this;
        }

        /// <summary>Adds a Media table with one row per cabinet name (null or empty means no cabinet column value).</summary>
        public TestMsi Media(params string[] cabinets)
        {
            _mediaCabinets = new List<string>(cabinets);
            return this;
        }

        public TestMsi Files(int count)
        {
            _fileCount = count;
            return this;
        }

        /// <summary>Adds a CustomAction table with an action that would start an executable if it ever ran.</summary>
        public TestMsi CustomActionThatWouldStartAProgram()
        {
            _withCustomAction = true;
            return this;
        }

        public string Save(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            IntPtr database;
            Check(Native.MsiOpenDatabase(path, new IntPtr(Native.CreateDirect), out database), "open");
            try
            {
                Execute(database, "CREATE TABLE `Property` (`Property` CHAR(72) NOT NULL, `Value` LONGCHAR NOT NULL PRIMARY KEY `Property`)");
                foreach (var pair in _properties)
                {
                    Insert(database, "INSERT INTO `Property` (`Property`, `Value`) VALUES (?, ?)", pair.Key, pair.Value);
                }

                if (_mediaCabinets != null)
                {
                    Execute(database, "CREATE TABLE `Media` (`DiskId` SHORT NOT NULL, `LastSequence` SHORT NOT NULL, `DiskPrompt` CHAR(64), `Cabinet` CHAR(255), `VolumeLabel` CHAR(32), `Source` CHAR(32) PRIMARY KEY `DiskId`)");
                    for (var i = 0; i < _mediaCabinets.Count; i++)
                    {
                        if (string.IsNullOrEmpty(_mediaCabinets[i]))
                        {
                            Insert(database, "INSERT INTO `Media` (`DiskId`, `LastSequence`) VALUES (?, ?)", i + 1, 100 * (i + 1));
                        }
                        else
                        {
                            Insert(database, "INSERT INTO `Media` (`DiskId`, `LastSequence`, `Cabinet`) VALUES (?, ?, ?)", i + 1, 100 * (i + 1), _mediaCabinets[i]);
                        }
                    }
                }

                if (_fileCount > 0)
                {
                    Execute(database, "CREATE TABLE `File` (`File` CHAR(72) NOT NULL, `Component_` CHAR(72) NOT NULL, `FileName` CHAR(255) NOT NULL, `FileSize` LONG NOT NULL, `Version` CHAR(72), `Language` CHAR(20), `Attributes` SHORT, `Sequence` SHORT NOT NULL PRIMARY KEY `File`)");
                    for (var i = 1; i <= _fileCount; i++)
                    {
                        Insert(database, "INSERT INTO `File` (`File`, `Component_`, `FileName`, `FileSize`, `Sequence`) VALUES (?, ?, ?, ?, ?)", "file" + i, "component", "file" + i + ".dat", 10, i);
                    }
                }

                if (_withCustomAction)
                {
                    Execute(database, "CREATE TABLE `CustomAction` (`Action` CHAR(72) NOT NULL, `Type` SHORT NOT NULL, `Source` CHAR(72), `Target` CHAR(255), `ExtendedType` LONG PRIMARY KEY `Action`)");
                    Insert(database, "INSERT INTO `CustomAction` (`Action`, `Type`, `Source`, `Target`) VALUES (?, ?, ?, ?)", "RunSomething", 50, "SystemFolder", "cmd.exe /c echo executed > ipb-test-marker.txt");
                }

                Check(Native.MsiDatabaseCommit(database), "commit");
            }
            finally
            {
                Native.MsiCloseHandle(database);
            }

            return path;
        }

        private static void Execute(IntPtr database, string sql)
        {
            IntPtr view;
            Check(Native.MsiDatabaseOpenView(database, sql, out view), "open view: " + sql);
            try
            {
                Check(Native.MsiViewExecute(view, IntPtr.Zero), "execute: " + sql);
                Native.MsiViewClose(view);
            }
            finally
            {
                Native.MsiCloseHandle(view);
            }
        }

        private static void Insert(IntPtr database, string sql, params object[] values)
        {
            IntPtr view;
            Check(Native.MsiDatabaseOpenView(database, sql, out view), "open view: " + sql);
            try
            {
                var record = Native.MsiCreateRecord((uint)values.Length);
                try
                {
                    for (var i = 0; i < values.Length; i++)
                    {
                        var field = (uint)(i + 1);
                        var text = values[i] as string;
                        if (text != null)
                        {
                            Check(Native.MsiRecordSetString(record, field, text), "set string");
                        }
                        else
                        {
                            Check(Native.MsiRecordSetInteger(record, field, Convert.ToInt32(values[i])), "set integer");
                        }
                    }

                    Check(Native.MsiViewExecute(view, record), "execute: " + sql);
                    Native.MsiViewClose(view);
                }
                finally
                {
                    Native.MsiCloseHandle(record);
                }
            }
            finally
            {
                Native.MsiCloseHandle(view);
            }
        }

        private static void Check(uint result, string what)
        {
            if (result != 0)
            {
                throw new InvalidOperationException("Test MSI creation failed (" + what + "): Windows Installer error " + result);
            }
        }

        private static class Native
        {
            public const int CreateDirect = 4;

            [DllImport("msi.dll", EntryPoint = "MsiOpenDatabaseW", CharSet = CharSet.Unicode, ExactSpelling = true)]
            public static extern uint MsiOpenDatabase(string path, IntPtr persist, out IntPtr database);

            [DllImport("msi.dll", EntryPoint = "MsiDatabaseOpenViewW", CharSet = CharSet.Unicode, ExactSpelling = true)]
            public static extern uint MsiDatabaseOpenView(IntPtr database, string query, out IntPtr view);

            [DllImport("msi.dll", ExactSpelling = true)]
            public static extern uint MsiViewExecute(IntPtr view, IntPtr record);

            [DllImport("msi.dll", ExactSpelling = true)]
            public static extern uint MsiViewClose(IntPtr view);

            [DllImport("msi.dll", ExactSpelling = true)]
            public static extern IntPtr MsiCreateRecord(uint parameters);

            [DllImport("msi.dll", EntryPoint = "MsiRecordSetStringW", CharSet = CharSet.Unicode, ExactSpelling = true)]
            public static extern uint MsiRecordSetString(IntPtr record, uint field, string value);

            [DllImport("msi.dll", ExactSpelling = true)]
            public static extern uint MsiRecordSetInteger(IntPtr record, uint field, int value);

            [DllImport("msi.dll", ExactSpelling = true)]
            public static extern uint MsiDatabaseCommit(IntPtr database);

            [DllImport("msi.dll", ExactSpelling = true)]
            public static extern uint MsiCloseHandle(IntPtr handle);
        }
    }
}
