using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using IntunePackageBuilder.Build.Packaging;

namespace IntunePackageBuilder.Build.Tests
{
    /// <summary>A temporary directory that is removed with the test.</summary>
    internal sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ipb-build-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; private set; }

        public string Combine(params string[] parts)
        {
            var result = Path;
            foreach (var part in parts)
            {
                result = System.IO.Path.Combine(result, part);
            }

            return result;
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, true);
                }
            }
            catch (IOException)
            {
                // A leftover temp folder must not fail a test.
            }
        }
    }

    internal static class TestPackages
    {
        /// <summary>Creates a ZIP with the structure of a <c>.intunewin</c> file.</summary>
        public static void WriteIntunewin(string path, bool withContent = true, bool withMetadata = true, bool emptyContent = false)
        {
            using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                if (withContent)
                {
                    WriteEntry(archive, IntunewinVerifier.ContentEntry, emptyContent ? new byte[0] : new byte[] { 1, 2, 3, 4 });
                }

                if (withMetadata)
                {
                    WriteEntry(archive, IntunewinVerifier.MetadataEntry, Encoding.UTF8.GetBytes("<ApplicationInfo/>"));
                }
            }
        }

        /// <summary>
        /// Writes a fake packaging tool as a batch file. It takes the arguments of the real tool
        /// (<c>-c folder -s file -o folder -q</c>) and copies a prepared package to the output folder.
        /// </summary>
        public static string WriteFakeTool(string path, string preparedPackage, int exitCode = 0, bool copyOutput = true, bool sleep = false)
        {
            var script = new StringBuilder();
            script.Append("@echo off\r\n");
            if (sleep)
            {
                script.Append("ping -n 30 127.0.0.1 >nul\r\n");
            }

            if (copyOutput)
            {
                script.Append("copy /Y \"").Append(preparedPackage).Append("\" \"%~6\\%~n4.intunewin\" >nul\r\n");
            }

            if (exitCode != 0)
            {
                script.Append("echo tool says no\r\n");
            }

            script.Append("exit /b ").Append(exitCode).Append("\r\n");
            File.WriteAllText(path, script.ToString(), Encoding.ASCII);
            return path;
        }

        private static void WriteEntry(ZipArchive archive, string name, byte[] data)
        {
            var entry = archive.CreateEntry(name);
            using (var stream = entry.Open())
            {
                stream.Write(data, 0, data.Length);
            }
        }
    }
}
