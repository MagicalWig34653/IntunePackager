using System;
using System.IO;

namespace IntunePackageBuilder.Core.Settings
{
    public enum BaseFolderStatus
    {
        /// <summary>The user has not chosen a base folder yet (first start).</summary>
        NotConfigured,

        /// <summary>The configured folder exists and can be written to.</summary>
        Ok,

        /// <summary>A folder is configured but is missing, not absolute or not reachable (for example an offline share).</summary>
        Unreachable,

        /// <summary>The folder exists but the current user cannot write to it.</summary>
        NotWritable
    }

    public sealed class BaseFolderResolution
    {
        public BaseFolderResolution(BaseFolderStatus status, string path)
        {
            Status = status;
            Path = path;
        }

        public BaseFolderStatus Status { get; private set; }

        /// <summary>
        /// The configured folder, or for <see cref="BaseFolderStatus.NotConfigured"/> the suggested default.
        /// A configured folder is never replaced by another one, even when it is unreachable.
        /// </summary>
        public string Path { get; private set; }
    }

    public static class BaseFolderResolver
    {
        public const string DefaultFolderName = "Intune-Paketprojekte";

        /// <summary>Suggested base folder for the first start: a folder below Documents. Not created here.</summary>
        public static string SuggestDefault()
        {
            return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), DefaultFolderName);
        }

        public static BaseFolderResolution Resolve(AppSettings settings)
        {
            return Resolve(settings, CanWrite);
        }

        internal static BaseFolderResolution Resolve(AppSettings settings, Func<string, bool> canWrite)
        {
            if (settings == null)
            {
                throw new ArgumentNullException("settings");
            }

            if (string.IsNullOrWhiteSpace(settings.BaseFolder))
            {
                return new BaseFolderResolution(BaseFolderStatus.NotConfigured, SuggestDefault());
            }

            var path = settings.BaseFolder;
            if (!System.IO.Path.IsPathRooted(path) || !Directory.Exists(path))
            {
                return new BaseFolderResolution(BaseFolderStatus.Unreachable, path);
            }

            return new BaseFolderResolution(canWrite(path) ? BaseFolderStatus.Ok : BaseFolderStatus.NotWritable, path);
        }

        /// <summary>Checks write access by creating a temporary file that is removed again.</summary>
        public static bool CanWrite(string directory)
        {
            try
            {
                var probe = System.IO.Path.Combine(directory, ".write-test-" + Guid.NewGuid().ToString("N"));
                using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose))
                {
                }

                return true;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
            catch (IOException)
            {
                return false;
            }
        }
    }
}
