using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;

namespace IntunePackageBuilder.Core.Storage
{
    /// <summary>Who holds a lock. Written by the holder so another process can show a useful message.</summary>
    public sealed class LockInfo
    {
        public int ProcessId { get; set; }

        public string MachineName { get; set; }

        public string UserName { get; set; }

        public DateTime AcquiredUtc { get; set; }
    }

    /// <summary>Another process (or another part of this process) already holds the lock.</summary>
    public sealed class LockHeldException : Exception
    {
        public LockHeldException(string lockPath, LockInfo holder)
            : base("The lock '" + lockPath + "' is held by another process.")
        {
            LockPath = lockPath;
            Holder = holder;
        }

        public string LockPath { get; private set; }

        /// <summary>Details of the holder, or null when they could not be read.</summary>
        public LockInfo Holder { get; private set; }
    }

    /// <summary>
    /// Exclusive lock for one project version directory. The lock is the open file handle, not the
    /// existence of the file: a crashed process releases it automatically, so an orphaned lock file
    /// never blocks anybody. The file is deleted when the lock is released.
    /// </summary>
    public sealed class VersionLock : IDisposable
    {
        public const string FileName = ".lock";

        private const int SharingViolation = 32;
        private const int LockViolation = 33;

        private FileStream _stream;

        private VersionLock(FileStream stream, string path)
        {
            _stream = stream;
            Path = path;
        }

        public string Path { get; private set; }

        /// <summary>Acquires the lock for a directory, creating the directory when needed.</summary>
        /// <exception cref="LockHeldException">The lock is held by someone else.</exception>
        public static VersionLock Acquire(string directory)
        {
            Directory.CreateDirectory(directory);
            var path = System.IO.Path.Combine(directory, FileName);

            FileStream stream;
            try
            {
                stream = new FileStream(
                    path,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.Read | FileShare.Delete,
                    4096,
                    FileOptions.DeleteOnClose);
            }
            catch (IOException ex) when (IsSharingProblem(ex))
            {
                throw new LockHeldException(path, TryReadHolder(path));
            }

            try
            {
                stream.SetLength(0);
                var info = new LockInfo
                {
                    ProcessId = CurrentProcessId(),
                    MachineName = Environment.MachineName,
                    UserName = Environment.UserName,
                    AcquiredUtc = DateTime.UtcNow
                };
                var bytes = new UTF8Encoding(false).GetBytes(JsonConvert.SerializeObject(info, JsonFormat.CreateSettings()));
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush();
            }
            catch
            {
                stream.Dispose();
                throw;
            }

            return new VersionLock(stream, path);
        }

        public void Dispose()
        {
            var stream = _stream;
            _stream = null;
            if (stream != null)
            {
                stream.Dispose();
            }
        }

        private static int CurrentProcessId()
        {
            using (var process = System.Diagnostics.Process.GetCurrentProcess())
            {
                return process.Id;
            }
        }

        private static bool IsSharingProblem(IOException ex)
        {
            var code = ex.HResult & 0xFFFF;
            return code == SharingViolation || code == LockViolation;
        }

        private static LockInfo TryReadHolder(string path)
        {
            try
            {
                using (var reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var text = new StreamReader(reader, Encoding.UTF8))
                {
                    return JsonConvert.DeserializeObject<LockInfo>(text.ReadToEnd(), JsonFormat.CreateSettings());
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is JsonException)
            {
                return null;
            }
        }
    }
}
