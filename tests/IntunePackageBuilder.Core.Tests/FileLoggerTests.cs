using System;
using System.IO;
using IntunePackageBuilder.Core.Logging;
using Xunit;

namespace IntunePackageBuilder.Core.Tests
{
    public sealed class FileLoggerTests : IDisposable
    {
        private static readonly DateTimeOffset Fixed = new DateTimeOffset(2026, 10, 8, 12, 30, 15, 123, TimeSpan.Zero);

        private readonly string _root = Path.Combine(Path.GetTempPath(), "ipb-log-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, true);
            }
            else if (File.Exists(_root))
            {
                File.Delete(_root);
            }
        }

        private FileLogger Create()
        {
            return new FileLogger(_root, "app", () => Fixed);
        }

        [Fact]
        public void Log_CreatesDirectoryAndDailyFile()
        {
            Create().Log(LogLevel.Info, "started");

            var file = Path.Combine(_root, "app-20261008.log");
            Assert.True(File.Exists(file));
            Assert.StartsWith("2026-10-08T12:30:15.123+00:00 [INFO] started", File.ReadAllText(file));
        }

        [Fact]
        public void Log_AppendsEntries()
        {
            var logger = Create();
            logger.Log(LogLevel.Info, "first");
            logger.Log(LogLevel.Error, "second");

            var lines = File.ReadAllLines(Path.Combine(_root, "app-20261008.log"));
            Assert.Equal(2, lines.Length);
            Assert.Contains("[ERROR] second", lines[1]);
        }

        [Fact]
        public void Log_EscapesLineBreaksInMessage()
        {
            Create().Log(LogLevel.Warning, "name\r\n2026-01-01T00:00:00.000+00:00 [INFO] forged");

            var lines = File.ReadAllLines(Path.Combine(_root, "app-20261008.log"));
            Assert.Single(lines);
            Assert.Contains("name\\r\\n2026", lines[0]);
        }

        [Fact]
        public void Log_WritesExceptionDetailsIndented()
        {
            Create().Log(LogLevel.Error, "failed", new InvalidOperationException("boom"));

            var text = File.ReadAllText(Path.Combine(_root, "app-20261008.log"));
            Assert.Contains("    System.InvalidOperationException: boom", text);
        }

        [Fact]
        public void Log_DoesNotThrowWhenDirectoryCannotBeCreated()
        {
            // A file occupies the path where the log directory should be.
            File.WriteAllText(_root, "not a directory");
            var logger = Create();

            logger.Log(LogLevel.Info, "ignored");

            Assert.NotNull(logger.LastWriteError);
        }

        [Fact]
        public void DefaultDirectory_IsBelowLocalAppData()
        {
            var expected = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Intune Package Builder",
                "Logs");

            Assert.Equal(expected, FileLogger.DefaultDirectory());
        }
    }
}
