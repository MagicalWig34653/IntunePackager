using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace IntunePackageBuilder.Core.Logging
{
    /// <summary>
    /// Writes one log file per day below a directory the current user can write to.
    /// Logging never throws: if the file cannot be written, the failure is kept in
    /// <see cref="LastWriteError"/> so callers can surface it.
    /// </summary>
    public sealed class FileLogger : ILogger
    {
        private readonly object _gate = new object();
        private readonly string _directory;
        private readonly string _baseName;
        private readonly Func<DateTimeOffset> _clock;

        public FileLogger(string directory, string baseName = "app", Func<DateTimeOffset> clock = null)
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                throw new ArgumentException("A log directory is required.", "directory");
            }

            _directory = directory;
            _baseName = baseName;
            _clock = clock ?? (() => DateTimeOffset.Now);
        }

        /// <summary>Last I/O problem while writing, or null when the last write succeeded.</summary>
        public Exception LastWriteError { get; private set; }

        /// <summary>Default location of the authoring tool log: %LOCALAPPDATA%\Intune Package Builder\Logs.</summary>
        public static string DefaultDirectory()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                AppInfo.ProductName,
                "Logs");
        }

        public void Log(LogLevel level, string message, Exception exception = null)
        {
            var now = _clock();
            var text = Format(now, level, message, exception);
            var path = Path.Combine(_directory, _baseName + "-" + now.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".log");

            lock (_gate)
            {
                try
                {
                    Directory.CreateDirectory(_directory);
                    File.AppendAllText(path, text, new UTF8Encoding(false));
                    LastWriteError = null;
                }
                catch (IOException ex)
                {
                    LastWriteError = ex;
                }
                catch (UnauthorizedAccessException ex)
                {
                    LastWriteError = ex;
                }
            }
        }

        /// <summary>
        /// Builds the log entry. Line breaks inside the message are escaped so that data
        /// (file names, metadata) cannot forge additional log lines; exception details follow
        /// on indented lines.
        /// </summary>
        internal static string Format(DateTimeOffset timestamp, LogLevel level, string message, Exception exception)
        {
            var builder = new StringBuilder();
            builder.Append(timestamp.ToString("yyyy-MM-ddTHH:mm:ss.fffzzz", CultureInfo.InvariantCulture));
            builder.Append(" [").Append(level.ToString().ToUpperInvariant()).Append("] ");
            builder.Append(Escape(message));
            builder.Append(Environment.NewLine);

            if (exception != null)
            {
                foreach (var line in exception.ToString().Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
                {
                    builder.Append("    ").Append(line).Append(Environment.NewLine);
                }
            }

            return builder.ToString();
        }

        private static string Escape(string message)
        {
            return (message ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n");
        }
    }
}
