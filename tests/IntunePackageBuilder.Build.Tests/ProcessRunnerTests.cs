using System;
using System.Diagnostics;
using IntunePackageBuilder.Build.Processes;
using Xunit;

namespace IntunePackageBuilder.Build.Tests
{
    public class ProcessRunnerTests
    {
        private static readonly string Cmd = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";

        [Fact]
        public void ExitCodeAndOutputAreReturned()
        {
            var result = ProcessRunner.Run(Cmd, new[] { "/c", "echo hello & exit 3" }, null, TimeSpan.FromSeconds(30));

            Assert.Equal(3, result.ExitCode);
            Assert.False(result.TimedOut);
            Assert.Contains("hello", result.StandardOutput);
        }

        [Fact]
        public void ErrorOutputIsCapturedSeparately()
        {
            var result = ProcessRunner.Run(Cmd, new[] { "/c", "echo problem 1>&2" }, null, TimeSpan.FromSeconds(30));

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("problem", result.StandardError);
            Assert.DoesNotContain("problem", result.StandardOutput);
        }

        [Fact]
        public void AProgramThatAsksForInputEndsBecauseStandardInputIsClosed()
        {
            // "set /p" reads a line from standard input; with a closed input it ends at once.
            var result = ProcessRunner.Run(Cmd, new[] { "/c", "set /p answer=Question: & echo done" }, null, TimeSpan.FromSeconds(30));

            Assert.False(result.TimedOut);
            Assert.Contains("done", result.StandardOutput);
        }

        [Fact]
        public void ALongRunningProgramIsStoppedAtTheTimeLimit()
        {
            var watch = Stopwatch.StartNew();

            var result = ProcessRunner.Run(Cmd, new[] { "/c", "ping -n 60 127.0.0.1 >nul" }, null, TimeSpan.FromSeconds(1));

            Assert.True(result.TimedOut);
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(30), "the limit must stop the program early");
        }

        [Fact]
        public void AMissingProgramThrows()
        {
            Assert.ThrowsAny<System.ComponentModel.Win32Exception>(() =>
                ProcessRunner.Run("C:\\does\\not\\exist.exe", new string[0], null, TimeSpan.FromSeconds(5)));
        }

        [Fact]
        public void TheWorkingDirectoryIsUsed()
        {
            using (var directory = new TempDirectory())
            {
                var result = ProcessRunner.Run(Cmd, new[] { "/c", "cd" }, directory.Path, TimeSpan.FromSeconds(30));

                // The temp path may be reported in its short or long form; the unique folder name is the same in both.
                Assert.Contains(System.IO.Path.GetFileName(directory.Path), result.StandardOutput, StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
