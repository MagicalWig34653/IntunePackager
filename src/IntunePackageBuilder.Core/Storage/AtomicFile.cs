using System;
using System.IO;
using System.Text;

namespace IntunePackageBuilder.Core.Storage
{
    /// <summary>
    /// Writes a file so that readers and crashes never observe a half-written file:
    /// content goes to a temporary file in the same directory, is flushed to disk, and then
    /// replaces the target in one step.
    /// </summary>
    public static class AtomicFile
    {
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        public static void WriteAllText(string path, string contents)
        {
            var fullPath = Path.GetFullPath(path);
            var directory = Path.GetDirectoryName(fullPath);
            Directory.CreateDirectory(directory);

            var temp = Path.Combine(directory, Path.GetFileName(fullPath) + "." + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    var bytes = Utf8NoBom.GetBytes(contents ?? string.Empty);
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }

                if (File.Exists(fullPath))
                {
                    File.Replace(temp, fullPath, null);
                }
                else
                {
                    File.Move(temp, fullPath);
                }
            }
            catch
            {
                TryDelete(temp);
                throw;
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (IOException)
            {
                // Best effort: the original error is more useful than a cleanup failure.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
