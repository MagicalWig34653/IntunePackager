using System;
using System.IO;
using System.Linq;
using FlaUI.Core.AutomationElements;
using Xunit;

namespace IntunePackageBuilder.UiTests
{
    /// <summary>
    /// Drives the real program (acceptance checks A01, A03, A04, A08, A09). The program is started with its own settings
    /// folder and English texts; the installer comes in through <c>--open</c>, which runs the same check as a drop.
    /// </summary>
    public class UiTests : IDisposable
    {
        private readonly string _folder = Path.Combine(Path.GetTempPath(), "ipb-ui-input-" + Guid.NewGuid().ToString("N"));

        public UiTests()
        {
            Directory.CreateDirectory(_folder);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(_folder, true);
            }
            catch (IOException)
            {
                // A leftover temp folder must not fail a test.
            }
        }

        /// <summary>A real EXE with version information, copied so the tests never touch the original. It is never started.</summary>
        private string InstallerCopy()
        {
            var source = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "notepad.exe");
            var target = Path.Combine(_folder, "setup.exe");
            File.Copy(source, target);
            return target;
        }

        private static void SetText(AutomationElement element, string text)
        {
            element.AsTextBox().Text = text;
        }

        [Fact]
        public void A01_TheFirstStartShowsTheStartPageWithoutProjects()
        {
            using (var app = new AppDriver())
            {
                Assert.NotNull(app.Require("DropArea"));
                Assert.NotNull(app.Require("ChooseInstaller"));
                Assert.NotNull(app.Require("ModeToggle"));
                Assert.NotNull(app.Require("BaseFolderText"));
                Assert.NotNull(app.Require("Projects"));
                Assert.Null(app.Find("Create"));
            }
        }

        [Fact]
        public void A04_AFileOfTheWrongTypeIsRejectedAtTheStartPage()
        {
            var text = Path.Combine(_folder, "readme.txt");
            File.WriteAllText(text, "not an installer");

            using (var app = new AppDriver(openPath: text))
            {
                Assert.NotNull(app.Require("StartMessage"));
                Assert.NotNull(app.Require("DropArea"));
                Assert.Null(app.Find("Create"));
            }
        }

        [Fact]
        public void A03_AnExeWithoutVendorInputCannotBeBuilt()
        {
            using (var app = new AppDriver(openPath: InstallerCopy()))
            {
                app.Require("Create").AsButton().Invoke();

                Assert.NotNull(app.Require("Problems"));
                Assert.NotNull(app.Require("InstallArguments"));
                Assert.NotNull(app.Require("UninstallProgram"));
                Assert.NotNull(app.Require("DetectionPath"));
                Assert.Null(app.Find("ResultHeading"));
                Assert.Null(app.Find("BuildProgress"));
            }
        }

        [Fact]
        public void A08_SwitchingTheModeKeepsTheEntries()
        {
            using (var app = new AppDriver(openPath: InstallerCopy()))
            {
                SetText(app.Require("Manufacturer"), "Contoso Ltd.");
                SetText(app.Require("InstallArguments"), "/quiet /norestart");
                Assert.Null(app.Find("Timeout"));

                app.Require("ModeToggle").AsToggleButton().Toggle();
                Assert.NotNull(app.Require("Timeout"));
                Assert.Equal("Contoso Ltd.", app.Require("Manufacturer").AsTextBox().Text);
                Assert.Equal("/quiet /norestart", app.Require("InstallArguments").AsTextBox().Text);

                app.Require("ModeToggle").AsToggleButton().Toggle();
                Assert.True(Wait.Until(() => app.Find("Timeout") == null, TimeSpan.FromSeconds(10)));
                Assert.Equal("Contoso Ltd.", app.Require("Manufacturer").AsTextBox().Text);
                Assert.Equal("/quiet /norestart", app.Require("InstallArguments").AsTextBox().Text);
            }
        }

        [Fact]
        public void A09_ABuildLocksTheEntriesAndEndsOnTheResultPage()
        {
            var tool = Environment.GetEnvironmentVariable("IPB_CONTENT_PREP_TOOL");
            Assert.False(string.IsNullOrEmpty(tool) || !File.Exists(tool), "IPB_CONTENT_PREP_TOOL must point to IntuneWinAppUtil.exe (the CI sets it).");
            var projects = Path.Combine(_folder, "projects");
            Directory.CreateDirectory(projects);

            using (var app = new AppDriver(openPath: InstallerCopy(), baseFolder: projects, toolPath: tool))
            {
                SetText(app.Require("SoftwareName"), "UI Test Editor");
                SetText(app.Require("Manufacturer"), "Contoso Ltd.");
                SetText(app.Require("TargetVersion"), "1.0.0");
                SetText(app.Require("InstallArguments"), "/quiet /norestart");
                SetText(app.Require("UninstallProgram"), "C:\\Program Files\\Contoso\\uninstall.exe");
                SetText(app.Require("UninstallArguments"), "/quiet");
                SetText(app.Require("DetectionPath"), "C:\\Program Files\\Contoso\\editor.exe");

                app.Require("Create").AsButton().Invoke();

                var lockedWhileBuilding = false;
                var finished = Wait.Until(
                    () =>
                    {
                        if (app.Find("ResultHeading") != null)
                        {
                            return true;
                        }

                        var field = app.Find("SoftwareName");
                        if (app.Find("BuildProgress") != null && field != null && !field.IsEnabled)
                        {
                            lockedWhileBuilding = true;
                        }

                        return false;
                    },
                    TimeSpan.FromMinutes(3));

                Assert.True(finished, "The build did not reach the result page.");
                Assert.True(lockedWhileBuilding, "The entries were never seen locked while the build ran.");

                var output = app.Require("OutputFolder").Name;
                Assert.True(Directory.Exists(output), output);
                Assert.NotEmpty(Directory.GetFiles(output, "*.intunewin", SearchOption.AllDirectories));
                Assert.NotEmpty(Directory.GetFiles(output, "*.html", SearchOption.AllDirectories));
            }
        }
    }
}
