using System;
using System.IO;
using System.Text;

namespace IntunePackageBuilder.Build.Pipeline
{
    /// <summary>
    /// The temporary working folder of one build. It carries a marker file with the build ID, and cleanup removes only
    /// a folder that has this marker: user originals and foreign folders are never deleted (SPEC section 7.3).
    /// </summary>
    public sealed class BuildWorkspace : IDisposable
    {
        public const string MarkerFileName = ".ipb-workspace";

        private readonly string _buildId;

        private BuildWorkspace(string root, string buildId)
        {
            Root = root;
            _buildId = buildId;
            PackageDirectory = Path.Combine(root, "package");
            IntuneDirectory = Path.Combine(root, "intune");
            OutputDirectory = Path.Combine(root, "output");
        }

        public string Root { get; private set; }

        /// <summary>Content of the package: runtime templates, <c>Files</c> and the wrapper configuration.</summary>
        public string PackageDirectory { get; private set; }

        public string IntuneDirectory { get; private set; }

        public string OutputDirectory { get; private set; }

        public static BuildWorkspace Create(string workRoot, string buildId)
        {
            var root = Path.Combine(Path.GetFullPath(workRoot), buildId);
            if (Directory.Exists(root) || File.Exists(root))
            {
                throw new IOException("The working folder already exists: " + root);
            }

            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, MarkerFileName), buildId, new UTF8Encoding(false));
            var workspace = new BuildWorkspace(root, buildId);
            Directory.CreateDirectory(workspace.PackageDirectory);
            Directory.CreateDirectory(workspace.IntuneDirectory);
            Directory.CreateDirectory(workspace.OutputDirectory);
            return workspace;
        }

        /// <summary>Removes the folder when it still carries this build's marker. Never throws; returns whether it is gone.</summary>
        public bool Cleanup()
        {
            return MarkedFolder.RemoveIfOwned(Root, MarkerFileName, _buildId);
        }

        public void Dispose()
        {
            Cleanup();
        }
    }

    /// <summary>Deletes a folder only when it carries a marker file with the expected content.</summary>
    internal static class MarkedFolder
    {
        public static bool RemoveIfOwned(string directory, string markerFileName, string expectedContent)
        {
            try
            {
                if (!Directory.Exists(directory))
                {
                    return true;
                }

                var marker = Path.Combine(directory, markerFileName);
                if (!File.Exists(marker) || (expectedContent != null && File.ReadAllText(marker).Trim() != expectedContent))
                {
                    return false;
                }

                for (var attempt = 0; attempt < 3; attempt++)
                {
                    try
                    {
                        ClearReadOnly(directory);
                        Directory.Delete(directory, true);
                        return true;
                    }
                    catch (IOException)
                    {
                        System.Threading.Thread.Sleep(200);
                    }
                    catch (UnauthorizedAccessException)
                    {
                        System.Threading.Thread.Sleep(200);
                    }
                }
            }
            catch (IOException)
            {
                // A leftover temporary folder must not hide the result or the real error of the build.
            }
            catch (UnauthorizedAccessException)
            {
                // See above.
            }

            return !Directory.Exists(directory);
        }

        private static void ClearReadOnly(string directory)
        {
            foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                var attributes = File.GetAttributes(file);
                if ((attributes & FileAttributes.ReadOnly) != 0)
                {
                    File.SetAttributes(file, attributes & ~FileAttributes.ReadOnly);
                }
            }
        }
    }
}
