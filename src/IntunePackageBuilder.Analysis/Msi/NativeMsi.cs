using System;
using System.Runtime.InteropServices;
using System.Text;

namespace IntunePackageBuilder.Analysis.Msi
{
    /// <summary>
    /// The few Windows Installer database functions needed to READ an MSI. Nothing here installs
    /// anything or runs custom actions: the database is only opened read-only and queried.
    /// </summary>
    internal static class NativeMsi
    {
        public const uint ErrorSuccess = 0;
        public const uint ErrorMoreData = 234;
        public const uint ErrorNoMoreItems = 259;
        public const uint ErrorBadQuerySyntax = 1615;
        public const uint ErrorInstallPackageOpenFailed = 1619;
        public const uint ErrorInstallPackageInvalid = 1620;

        /// <summary>MSIDBOPEN_READONLY: the persist argument is the integer 0 passed as a pointer.</summary>
        public static readonly IntPtr OpenReadOnly = IntPtr.Zero;

        [DllImport("msi.dll", EntryPoint = "MsiOpenDatabaseW", CharSet = CharSet.Unicode, ExactSpelling = true)]
        public static extern uint MsiOpenDatabase(string databasePath, IntPtr persist, out IntPtr database);

        [DllImport("msi.dll", EntryPoint = "MsiDatabaseOpenViewW", CharSet = CharSet.Unicode, ExactSpelling = true)]
        public static extern uint MsiDatabaseOpenView(IntPtr database, string query, out IntPtr view);

        [DllImport("msi.dll", ExactSpelling = true)]
        public static extern uint MsiViewExecute(IntPtr view, IntPtr record);

        [DllImport("msi.dll", ExactSpelling = true)]
        public static extern uint MsiViewFetch(IntPtr view, out IntPtr record);

        [DllImport("msi.dll", ExactSpelling = true)]
        public static extern uint MsiViewClose(IntPtr view);

        [DllImport("msi.dll", EntryPoint = "MsiRecordGetStringW", CharSet = CharSet.Unicode, ExactSpelling = true)]
        public static extern uint MsiRecordGetString(IntPtr record, uint field, StringBuilder value, ref uint valueLength);

        [DllImport("msi.dll", ExactSpelling = true)]
        public static extern uint MsiCloseHandle(IntPtr handle);
    }

    /// <summary>Closes a Windows Installer handle when disposed.</summary>
    internal sealed class MsiHandle : IDisposable
    {
        private IntPtr _handle;

        public MsiHandle(IntPtr handle)
        {
            _handle = handle;
        }

        public IntPtr Value
        {
            get { return _handle; }
        }

        public void Dispose()
        {
            if (_handle != IntPtr.Zero)
            {
                NativeMsi.MsiCloseHandle(_handle);
                _handle = IntPtr.Zero;
            }
        }
    }
}
