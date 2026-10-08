using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using IntunePackageBuilder.Core.Storage;
using Xunit;

namespace IntunePackageBuilder.Core.Tests
{
    public sealed class VersionLockTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "ipb-lock-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, true);
            }
        }

        private string Dir(string name)
        {
            return Path.Combine(_root, name);
        }

        [Fact]
        public void Acquire_CreatesTheDirectoryAndTheLockFile()
        {
            var directory = Dir("a\\b");

            using (var held = VersionLock.Acquire(directory))
            {
                Assert.True(Directory.Exists(directory));
                Assert.Equal(Path.Combine(directory, ".lock"), held.Path);
                Assert.True(File.Exists(held.Path));
            }
        }

        [Fact]
        public void Acquire_FailsWhileAnotherHolderHasTheLock_AndNamesTheHolder()
        {
            var directory = Dir("v1");

            using (VersionLock.Acquire(directory))
            {
                var ex = Assert.Throws<LockHeldException>(() => VersionLock.Acquire(directory));

                Assert.NotNull(ex.Holder);
                Assert.Equal(Process.GetCurrentProcess().Id, ex.Holder.ProcessId);
                Assert.Equal(Environment.MachineName, ex.Holder.MachineName);
                Assert.Equal(Path.Combine(directory, ".lock"), ex.LockPath);
            }
        }

        [Fact]
        public void Dispose_ReleasesTheLockAndRemovesTheFile()
        {
            var directory = Dir("v1");
            var first = VersionLock.Acquire(directory);
            var path = first.Path;

            first.Dispose();
            first.Dispose();

            Assert.False(File.Exists(path));
            using (VersionLock.Acquire(directory))
            {
                Assert.True(File.Exists(path));
            }
        }

        [Fact]
        public void LocksOfDifferentVersionsAreIndependent()
        {
            using (VersionLock.Acquire(Dir("v1")))
            using (VersionLock.Acquire(Dir("v2")))
            {
                Assert.True(Directory.Exists(Dir("v2")));
            }
        }

        [Fact]
        public void AnOrphanedLockFileDoesNotBlock()
        {
            // A crashed process leaves the file behind but no handle: the lock is the handle, not the file.
            var directory = Dir("v1");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, ".lock"), "{\"processId\":1,\"userName\":\"crashed\"} and some stale garbage");

            using (var held = VersionLock.Acquire(directory))
            {
                var content = ReadWhileLocked(held.Path);
                Assert.DoesNotContain("garbage", content);
                Assert.Contains("\"processId\"", content);
            }
        }

        [Fact]
        public void AnotherProcessCannotAcquireWhileThisOneHoldsTheLock_AndViceVersa()
        {
            var directory = Dir("shared");
            var ready = Path.Combine(_root, "ready.txt");
            var release = Path.Combine(_root, "release.txt");
            Directory.CreateDirectory(_root);
            var script = Path.Combine(_root, "hold-lock.ps1");
            File.WriteAllText(script, ChildScript);
            var coreDll = typeof(VersionLock).Assembly.Location;

            var info = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"" + script + "\""
                    + " -CoreDll \"" + coreDll + "\" -VersionDir \"" + directory + "\""
                    + " -ReadyFile \"" + ready + "\" -ReleaseFile \"" + release + "\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };

            using (var child = Process.Start(info))
            {
                try
                {
                    WaitForFile(ready, child);
                    var childPid = int.Parse(File.ReadAllText(ready).Trim());

                    // The child holds the lock: this process must be refused and see the child as holder.
                    var ex = Assert.Throws<LockHeldException>(() => VersionLock.Acquire(directory));
                    Assert.Equal(childPid, ex.Holder.ProcessId);
                    Assert.NotEqual(Process.GetCurrentProcess().Id, ex.Holder.ProcessId);

                    File.WriteAllText(release, "go");
                    Assert.True(child.WaitForExit(30000), "The child process did not finish.");
                    Assert.Equal(0, child.ExitCode);
                }
                finally
                {
                    if (!child.HasExited)
                    {
                        child.Kill();
                    }
                }
            }

            // After the child released the lock, this process can take it.
            using (VersionLock.Acquire(directory))
            {
                Assert.True(Directory.Exists(directory));
            }
        }

        // The holder keeps the file open for writing, so a reader must allow writers.
        private static string ReadWhileLocked(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            {
                return reader.ReadToEnd();
            }
        }

        private static void WaitForFile(string path, Process child)
        {
            var deadline = DateTime.UtcNow.AddSeconds(60);
            while (!File.Exists(path))
            {
                if (child.HasExited)
                {
                    Assert.Fail("The child process ended early (exit code " + child.ExitCode + "): " + child.StandardError.ReadToEnd());
                }

                if (DateTime.UtcNow > deadline)
                {
                    Assert.Fail("The child process did not acquire the lock in time.");
                }

                Thread.Sleep(100);
            }

            // The file is written in one call; give the child a moment to finish writing it.
            Thread.Sleep(200);
        }

        private const string ChildScript = @"param([string]$CoreDll, [string]$VersionDir, [string]$ReadyFile, [string]$ReleaseFile)
$ErrorActionPreference = 'Stop'
[System.Reflection.Assembly]::LoadFrom($CoreDll) | Out-Null
$lock = [IntunePackageBuilder.Core.Storage.VersionLock]::Acquire($VersionDir)
[System.IO.File]::WriteAllText($ReadyFile, [string]$PID)
$deadline = (Get-Date).AddSeconds(90)
while (-not (Test-Path $ReleaseFile) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 100 }
$lock.Dispose()
";
    }
}
