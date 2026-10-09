using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;

namespace IntunePackageBuilder.UiTests
{
    /// <summary>Starts the real program with its own settings folder and gives access to its controls by automation ID.</summary>
    internal sealed class AppDriver : IDisposable
    {
        private readonly string _root;
        private readonly UIA3Automation _automation = new UIA3Automation();
        private readonly Process _process;

        public AppDriver(string openPath = null, string baseFolder = null, string toolPath = null)
        {
            _root = Path.Combine(Path.GetTempPath(), "ipb-ui-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
            SettingsDirectory = Path.Combine(_root, "settings");
            Directory.CreateDirectory(SettingsDirectory);
            if (baseFolder != null || toolPath != null)
            {
                File.WriteAllText(Path.Combine(SettingsDirectory, "settings.json"), SettingsJson(baseFolder, toolPath));
            }

            var arguments = "--language en --settings-dir \"" + SettingsDirectory + "\"";
            if (openPath != null)
            {
                arguments += " --open \"" + openPath + "\"";
            }

            var info = new ProcessStartInfo(ExecutablePath(), arguments) { UseShellExecute = false };
            _process = Process.Start(info);
            Window = Wait.For(() =>
            {
                _process.Refresh();
                return _process.HasExited || _process.MainWindowHandle == IntPtr.Zero ? null : _automation.FromHandle(_process.MainWindowHandle).AsWindow();
            }, TimeSpan.FromSeconds(60), "the main window");
        }

        public string Root
        {
            get { return _root; }
        }

        public string SettingsDirectory { get; private set; }

        public Window Window { get; private set; }

        /// <summary>The control with this automation ID, or null while it is not shown (hidden controls are not in the tree).</summary>
        public AutomationElement Find(string automationId)
        {
            return Window.FindFirstDescendant(factory => factory.ByAutomationId(automationId));
        }

        public AutomationElement Require(string automationId, int seconds = 20)
        {
            return Wait.For(() => Find(automationId), TimeSpan.FromSeconds(seconds), automationId);
        }

        public void Dispose()
        {
            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill();
                    _process.WaitForExit(10000);
                }
            }
            catch (InvalidOperationException)
            {
                // The process ended on its own.
            }

            _automation.Dispose();
            try
            {
                Directory.Delete(_root, true);
            }
            catch (IOException)
            {
                // A leftover temp folder must not fail a test.
            }
        }

        private static string SettingsJson(string baseFolder, string toolPath)
        {
            var text = "{ \"schemaVersion\": 1";
            if (baseFolder != null)
            {
                text += ", \"baseFolder\": " + Quote(baseFolder);
            }

            if (toolPath != null)
            {
                text += ", \"contentPrepToolPath\": " + Quote(toolPath);
            }

            return text + " }";
        }

        private static string Quote(string value)
        {
            return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }

        private static string ExecutablePath()
        {
            var fromEnvironment = Environment.GetEnvironmentVariable("IPB_APP_EXE");
            if (!string.IsNullOrEmpty(fromEnvironment) && File.Exists(fromEnvironment))
            {
                return fromEnvironment;
            }

            // Walk up from the test output to the repository root and take the Release build of the program.
            var directory = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (directory != null)
            {
                foreach (var configuration in new[] { "Release", "Debug" })
                {
                    var candidate = Path.Combine(directory.FullName, "src", "IntunePackageBuilder.App", "bin", configuration, "net48", "IntunePackageBuilder.exe");
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }

                directory = directory.Parent;
            }

            throw new FileNotFoundException("IntunePackageBuilder.exe was not found; build the App project or set IPB_APP_EXE.");
        }
    }

    internal static class Wait
    {
        /// <summary>Polls until the function returns a value; fails with the name of what was awaited.</summary>
        public static T For<T>(Func<T> probe, TimeSpan timeout, string what) where T : class
        {
            var end = DateTime.UtcNow + timeout;
            while (true)
            {
                var value = probe();
                if (value != null)
                {
                    return value;
                }

                if (DateTime.UtcNow > end)
                {
                    throw new TimeoutException("Timed out waiting for " + what);
                }

                Thread.Sleep(200);
            }
        }

        public static bool Until(Func<bool> condition, TimeSpan timeout)
        {
            var end = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow <= end)
            {
                if (condition())
                {
                    return true;
                }

                Thread.Sleep(200);
            }

            return false;
        }
    }
}
