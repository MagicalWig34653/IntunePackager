using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using IntunePackageBuilder.App.Infrastructure;
using IntunePackageBuilder.App.Services;
using IntunePackageBuilder.Build.Packaging;
using IntunePackageBuilder.Build.Workflow;
using IntunePackageBuilder.Core.Settings;
using IntunePackageBuilder.Core.Sources;
using IntunePackageBuilder.Core.Versions;
using Xunit;

// The program language is global state; tests that read texts must not run side by side.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace IntunePackageBuilder.App.Tests
{
    internal sealed class FakeDialogs : IUserDialogs
    {
        public string FileToPick { get; set; }

        public string FolderToPick { get; set; }

        public bool Answer { get; set; }

        public List<string> Questions { get; private set; } = new List<string>();

        public string PickFile(string title, string filter, string initialFolder)
        {
            return FileToPick;
        }

        public string PickFolder(string title, string initialFolder)
        {
            return FolderToPick;
        }

        public bool Confirm(string title, string message)
        {
            Questions.Add(message);
            return Answer;
        }
    }

    internal sealed class FakeShell : IShell
    {
        public List<string> Opened { get; private set; } = new List<string>();

        public string Copied { get; private set; }

        public void OpenFolder(string path)
        {
            Opened.Add(path);
        }

        public void OpenFile(string path)
        {
            Opened.Add(path);
        }

        public void OpenUrl(string url)
        {
            Opened.Add(url);
        }

        public void CopyText(string text)
        {
            Copied = text;
        }
    }

    /// <summary>Stands in for the packaging tool: writes a valid package, or fails on request.</summary>
    internal sealed class FakeRunner : IContentPrepRunner
    {
        public int Calls { get; private set; }

        public Exception FailWith { get; set; }

        public string Pack(PackRequest request)
        {
            Calls++;
            if (FailWith != null)
            {
                throw FailWith;
            }

            Directory.CreateDirectory(request.OutputFolder);
            var output = Path.Combine(request.OutputFolder, Path.GetFileNameWithoutExtension(request.SetupFile) + ".intunewin");
            using (var stream = new FileStream(output, FileMode.Create, FileAccess.Write))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                Write(archive, IntunewinVerifier.ContentEntry, new byte[] { 1, 2, 3, 4 });
                Write(archive, IntunewinVerifier.MetadataEntry, Encoding.UTF8.GetBytes("<ApplicationInfo/>"));
            }

            return output;
        }

        private static void Write(ZipArchive archive, string name, byte[] data)
        {
            using (var stream = archive.CreateEntry(name).Open())
            {
                stream.Write(data, 0, data.Length);
            }
        }
    }

    /// <summary>Everything a view model test needs in a temporary folder: services with fakes, a base folder, an installer and a tool stand-in.</summary>
    internal sealed class AppFixture : IDisposable
    {
        private readonly string _root;

        public AppFixture(string culture = "en")
        {
            Loc.Use(new CultureInfo(culture));
            _root = Path.Combine(Path.GetTempPath(), "ipb-app-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
            BaseFolder = Path.Combine(_root, "projects");
            Directory.CreateDirectory(BaseFolder);
            TemplateDirectory = Path.Combine(_root, "template");
            Directory.CreateDirectory(TemplateDirectory);
            File.WriteAllText(Path.Combine(TemplateDirectory, "Install.cmd"), "@echo off\r\n");
            ToolPath = Path.Combine(_root, "IntuneWinAppUtil.exe");
            File.WriteAllText(ToolPath, "stand-in for the packaging tool");
            InstallerPath = Path.Combine(_root, "drop", "setup.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(InstallerPath));
            File.WriteAllBytes(InstallerPath, new byte[] { 0x4D, 0x5A, 1, 2, 3 });
            Dialogs = new FakeDialogs();
            Shell = new FakeShell();
            Runner = new FakeRunner();
            Services = new AppServices(new SettingsStore(Path.Combine(_root, "settings")), Dialogs, Shell, Runner, TemplateDirectory);
            Services.WorkRoot = Path.Combine(_root, "work");
            Services.Settings.BaseFolder = BaseFolder;
        }

        public string BaseFolder { get; private set; }

        public string TemplateDirectory { get; private set; }

        public string ToolPath { get; private set; }

        public string InstallerPath { get; private set; }

        public FakeDialogs Dialogs { get; private set; }

        public FakeShell Shell { get; private set; }

        public FakeRunner Runner { get; private set; }

        public AppServices Services { get; private set; }

        public string ToolkitZipPath { get; private set; }

        public string ToolkitSha256 { get; private set; }

        /// <summary>Writes a minimal toolkit ZIP and its pin file, points the services at them and the settings at the ZIP.</summary>
        public void UseToolkitZip(bool pinned)
        {
            ToolkitZipPath = Path.Combine(_root, "PSAppDeployToolkit_Template_v4.zip");
            using (var stream = new FileStream(ToolkitZipPath, FileMode.Create, FileAccess.Write))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                foreach (var name in new[]
                {
                    "PSAppDeployToolkit/PSAppDeployToolkit.psd1", "PSAppDeployToolkit/PSAppDeployToolkit.psm1", "PSAppDeployToolkit/COPYING.Lesser",
                    "PSAppDeployToolkit/Config/config.psd1", "PSAppDeployToolkit/Strings/strings.psd1", "PSAppDeployToolkit/lib/PSADT.dll"
                })
                {
                    Write(archive, name, Encoding.UTF8.GetBytes("content of " + name));
                }
            }

            ToolkitSha256 = ContentPrepTool.ComputeSha256(ToolkitZipPath);
            var pinPath = Path.Combine(_root, "psadt.json");
            File.WriteAllText(
                pinPath,
                "{\"schemaVersion\":1,\"versions\":[" + (pinned ? "{\"version\":\"9.9.9\",\"asset\":\"PSAppDeployToolkit_Template_v4.zip\",\"sha256\":\"" + ToolkitSha256 + "\",\"commit\":\"" + new string('b', 40) + "\"}" : string.Empty) + "]}",
                new UTF8Encoding(false));
            var templateDirectory = Path.Combine(_root, "psadt-template");
            Directory.CreateDirectory(templateDirectory);
            File.WriteAllText(Path.Combine(templateDirectory, "Install.cmd"), "@echo off\r\n");
            File.WriteAllText(Path.Combine(templateDirectory, "Invoke-AppDeployToolkit.ps1"), "# entry\r\n");
            Services.PsadtTemplateDirectory = templateDirectory;
            Services.PsadtPinPath = pinPath;
            Services.Settings.PsadtPackagePath = ToolkitZipPath;
        }

        public string WriteImage(string name, bool png = true)
        {
            var path = Path.Combine(_root, "drop", name);
            File.WriteAllBytes(path, png ? new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2 } : new byte[] { 1, 2, 3 });
            return path;
        }

        private static void Write(ZipArchive archive, string name, byte[] data)
        {
            using (var stream = archive.CreateEntry(name).Open())
            {
                stream.Write(data, 0, data.Length);
            }
        }

        public SourceAnalysis ExeAnalysis()
        {
            return new SourceAnalysis
            {
                Item = new DroppedItem(DroppedKind.Exe, InstallerPath),
                InstallerType = InstallerType.Exe,
                ImportKind = ImportKind.SingleFile,
                InstallerRelativePath = "setup.exe",
                SuggestedName = "Fabrikam Editor",
                SuggestedManufacturer = "Fabrikam",
                SuggestedVersion = "12.0.3",
                Notes = new AnalysisNote[0]
            };
        }

        public SourceAnalysis MsiAnalysis()
        {
            return new SourceAnalysis
            {
                Item = new DroppedItem(DroppedKind.Msi, Path.Combine(_root, "drop", "reader.msi")),
                InstallerType = InstallerType.Msi,
                ImportKind = ImportKind.SingleFile,
                InstallerRelativePath = "reader.msi",
                SuggestedName = "Contoso Reader",
                SuggestedManufacturer = "Contoso Ltd.",
                SuggestedVersion = "4.2.1",
                ProductCode = "{8F2C41A7-5B3E-4D19-A7C0-3E6D21B94F05}",
                Notes = new AnalysisNote[0]
            };
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_root))
                {
                    Directory.Delete(_root, true);
                }
            }
            catch (IOException)
            {
                // A leftover temp folder must not fail a test.
            }
        }
    }
}
