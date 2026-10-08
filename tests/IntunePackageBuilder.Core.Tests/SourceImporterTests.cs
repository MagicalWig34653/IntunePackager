using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using IntunePackageBuilder.Core.Projects;
using IntunePackageBuilder.Core.Sources;
using IntunePackageBuilder.Core.Versions;
using Xunit;

namespace IntunePackageBuilder.Core.Tests
{
    public sealed class SourceImporterTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "ipb-import-" + Guid.NewGuid().ToString("N"));
        private readonly string _vendor;
        private readonly string _target;

        public SourceImporterTests()
        {
            _vendor = Path.Combine(_root, "vendor");
            _target = Path.Combine(_root, "project", "versions", "1.0", "source");
            Directory.CreateDirectory(_vendor);
        }

        public void Dispose()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, true);
            }
        }

        private string Put(string relative, string content)
        {
            var path = Path.Combine(_vendor, relative.Replace('/', '\\'));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, new UTF8Encoding(false).GetBytes(content));
            return path;
        }

        private static ImportProblem ProblemOf(Action action)
        {
            return Assert.Throws<ImportRejectedException>(action).Problem;
        }

        private void AssertNothingCreatedBelow(string directory)
        {
            Assert.False(Directory.Exists(_target), "The target must not exist after a refused import.");
            Assert.Empty(Directory.Exists(directory) ? Directory.GetFileSystemEntries(directory, "*", SearchOption.AllDirectories) : new string[0]);
        }

        // ---- Classify (A04, logic) ----

        [Fact]
        public void Classify_AcceptsOneMsiOrExeIgnoringTheCaseOfTheExtension()
        {
            var msi = Put("setup.MSI", "m");
            var exe = Put("setup.exe", "e");

            Assert.Equal(DroppedKind.Msi, SourceImporter.Classify(new[] { msi }, false).Kind);
            Assert.Equal(DroppedKind.Exe, SourceImporter.Classify(new[] { exe }, false).Kind);
        }

        [Fact]
        public void Classify_GivesAFullPath()
        {
            var msi = Put("setup.msi", "m");

            Assert.Equal(msi, SourceImporter.Classify(new[] { msi }, false).Path);
        }

        [Fact]
        public void Classify_RejectsNothingAndMultipleItems()
        {
            var a = Put("a.msi", "a");
            var b = Put("b.msi", "b");

            Assert.Equal(ImportProblem.NothingDropped, ProblemOf(() => SourceImporter.Classify(new string[0], false)));
            Assert.Equal(ImportProblem.NothingDropped, ProblemOf(() => SourceImporter.Classify(null, false)));
            Assert.Equal(ImportProblem.NothingDropped, ProblemOf(() => SourceImporter.Classify(new[] { " " }, false)));
            Assert.Equal(ImportProblem.MultipleItems, ProblemOf(() => SourceImporter.Classify(new[] { a, b }, false)));
            Assert.Equal(ImportProblem.MultipleItems, ProblemOf(() => SourceImporter.Classify(new[] { a, _vendor }, true)));
        }

        [Theory]
        [InlineData("readme.txt")]
        [InlineData("setup.msix")]
        [InlineData("setup.msi.txt")]
        [InlineData("setup")]
        [InlineData("script.ps1")]
        public void Classify_RejectsOtherFileTypes(string name)
        {
            var path = Put(name, "x");

            Assert.Equal(ImportProblem.UnsupportedFileType, ProblemOf(() => SourceImporter.Classify(new[] { path }, true)));
        }

        [Fact]
        public void Classify_RejectsAMissingPath()
        {
            Assert.Equal(ImportProblem.NotFound, ProblemOf(() => SourceImporter.Classify(new[] { Path.Combine(_root, "missing.msi") }, false)));
        }

        [Fact]
        public void Classify_AcceptsAFolderOnlyWhenAllowed()
        {
            Assert.Equal(ImportProblem.FolderNotAllowed, ProblemOf(() => SourceImporter.Classify(new[] { _vendor }, false)));

            var item = SourceImporter.Classify(new[] { _vendor }, true);

            Assert.Equal(DroppedKind.Folder, item.Kind);
            Assert.Equal(_vendor, item.Path);
        }

        // ---- Single file ----

        [Fact]
        public void ImportFile_StoresTheFileAndReturnsItsManifest()
        {
            var msi = Put("setup.msi", "abc");

            var result = SourceImporter.ImportFile(msi, _target);

            Assert.Equal("setup.msi", result.InstallerRelativePath);
            Assert.Equal("abc", File.ReadAllText(Path.Combine(_target, "setup.msi")));
            Assert.Equal("setup.msi", result.Manifest.Files.Single().Path);
            Assert.Equal(3, result.TotalBytes);
            Assert.Empty(SourceManifestBuilder.Compare(_target, result.Manifest));
        }

        [Fact]
        public void ImportFile_CopiesOnlyTheChosenFileNotItsNeighbours()
        {
            var msi = Put("setup.msi", "abc");
            Put("other.dll", "neighbour");

            SourceImporter.ImportFile(msi, _target);

            Assert.Equal(new[] { "setup.msi" }, Directory.GetFiles(_target).Select(Path.GetFileName).ToArray());
        }

        [Fact]
        public void ImportFile_LeavesTheOriginalUntouched()
        {
            var msi = Put("setup.msi", "abc");
            var before = SourceManifestBuilder.Build(_vendor);

            SourceImporter.ImportFile(msi, _target);

            Assert.Empty(SourceManifestBuilder.Compare(_vendor, before));
        }

        [Fact]
        public void ImportFile_LeavesNoStagingFolderBehind()
        {
            var msi = Put("setup.msi", "abc");

            SourceImporter.ImportFile(msi, _target);

            var parent = Path.GetDirectoryName(_target);
            Assert.Equal(new[] { "source" }, Directory.GetDirectories(parent).Select(Path.GetFileName).ToArray());
        }

        [Fact]
        public void ImportFile_RejectsAnUnsupportedType()
        {
            var txt = Put("notes.txt", "x");

            Assert.Equal(ImportProblem.UnsupportedFileType, ProblemOf(() => SourceImporter.ImportFile(txt, _target)));
            Assert.False(Directory.Exists(_target));
        }

        [Fact]
        public void ImportFile_RejectsAFileThatLivesInsideTheTarget()
        {
            var inside = Path.Combine(_target, "setup.msi");
            Directory.CreateDirectory(_target);
            File.WriteAllText(inside, "abc");

            Assert.Equal(ImportProblem.SourceInsideTarget, ProblemOf(() => SourceImporter.ImportFile(inside, _target)));
            Assert.Equal("abc", File.ReadAllText(inside));
        }

        [Fact]
        public void ImportFile_RefusesANonEmptyTargetAndKeepsItsContent()
        {
            var msi = Put("setup.msi", "abc");
            Directory.CreateDirectory(_target);
            File.WriteAllText(Path.Combine(_target, "existing.msi"), "stored earlier");

            Assert.Equal(ImportProblem.TargetNotEmpty, ProblemOf(() => SourceImporter.ImportFile(msi, _target)));

            Assert.Equal("stored earlier", File.ReadAllText(Path.Combine(_target, "existing.msi")));
            Assert.Equal(new[] { "existing.msi" }, Directory.GetFiles(_target).Select(Path.GetFileName).ToArray());
        }

        [Fact]
        public void ImportFile_AcceptsAnExistingEmptyTarget()
        {
            var msi = Put("setup.msi", "abc");
            Directory.CreateDirectory(_target);

            SourceImporter.ImportFile(msi, _target);

            Assert.True(File.Exists(Path.Combine(_target, "setup.msi")));
        }

        // ---- Folder ----

        [Fact]
        public void ImportFolder_CopiesTheWholeTreeAndNamesTheInstaller()
        {
            Put("setup.msi", "abc");
            Put("data1.cab", "cabinet");
            Put("redist/vc/vcredist.exe", "redist");
            Put("docs/readme.txt", "docs");

            var result = SourceImporter.ImportFolder(_vendor, "setup.msi", _target);

            Assert.Equal("setup.msi", result.InstallerRelativePath);
            Assert.Equal(new[] { "data1.cab", "docs/readme.txt", "redist/vc/vcredist.exe", "setup.msi" },
                result.Manifest.Files.Select(f => f.Path).ToArray());
            Assert.Empty(SourceManifestBuilder.Compare(_target, result.Manifest));
            Assert.Equal("redist", File.ReadAllText(Path.Combine(_target, "redist", "vc", "vcredist.exe")));
        }

        [Fact]
        public void ImportFolder_AcceptsAnInstallerInASubfolderWithEitherSeparator()
        {
            Put("app/setup.exe", "exe");

            var result = SourceImporter.ImportFolder(_vendor, "app\\setup.exe", _target);

            Assert.Equal("app/setup.exe", result.InstallerRelativePath);
        }

        [Fact]
        public void ImportFolder_LeavesTheOriginalUntouched()
        {
            Put("setup.msi", "abc");
            Put("sub/a.dll", "a");
            var before = SourceManifestBuilder.Build(_vendor);

            SourceImporter.ImportFolder(_vendor, "setup.msi", _target);

            Assert.Empty(SourceManifestBuilder.Compare(_vendor, before));
        }

        [Theory]
        [InlineData("..\\outside.msi")]
        [InlineData("sub\\..\\..\\outside.msi")]
        [InlineData("C:\\Windows\\setup.msi")]
        [InlineData("\\\\server\\share\\setup.msi")]
        [InlineData("/etc/setup.msi")]
        public void ImportFolder_RejectsAnInstallerOutsideTheFolder(string installer)
        {
            Put("setup.msi", "abc");

            Assert.Equal(ImportProblem.InstallerOutsideSource, ProblemOf(() => SourceImporter.ImportFolder(_vendor, installer, _target)));
            AssertNothingCreatedBelow(_target);
        }

        [Fact]
        public void ImportFolder_RejectsAMissingInstaller()
        {
            Put("setup.msi", "abc");

            Assert.Equal(ImportProblem.InstallerNotFound, ProblemOf(() => SourceImporter.ImportFolder(_vendor, "missing.msi", _target)));
            Assert.Equal(ImportProblem.InstallerNotFound, ProblemOf(() => SourceImporter.ImportFolder(_vendor, "", _target)));
            Assert.False(Directory.Exists(_target));
        }

        [Fact]
        public void ImportFolder_RejectsAnInstallerWithAnUnsupportedType()
        {
            Put("setup.zip", "abc");

            Assert.Equal(ImportProblem.UnsupportedFileType, ProblemOf(() => SourceImporter.ImportFolder(_vendor, "setup.zip", _target)));
            Assert.False(Directory.Exists(_target));
        }

        [Fact]
        public void ImportFolder_RejectsAMissingFolder()
        {
            Assert.Equal(ImportProblem.NotFound, ProblemOf(() => SourceImporter.ImportFolder(Path.Combine(_root, "nope"), "setup.msi", _target)));
        }

        // ---- A12: recursion, links, path length ----

        [Fact]
        public void ImportFolder_RejectsASourceThatContainsTheTargetBeforeCopyingAnything()
        {
            // The vendor folder is the parent of the project: copying it would copy into itself.
            Put("setup.msi", "abc");
            var targetInsideVendor = Path.Combine(_vendor, "project", "versions", "1.0", "source");
            var before = SourceManifestBuilder.Build(_vendor);

            Assert.Equal(ImportProblem.TargetInsideSource, ProblemOf(() => SourceImporter.ImportFolder(_vendor, "setup.msi", targetInsideVendor)));

            Assert.False(Directory.Exists(Path.Combine(_vendor, "project")));
            Assert.Empty(SourceManifestBuilder.Compare(_vendor, before));
        }

        [Fact]
        public void ImportFolder_RejectsASourceThatContainsTheWholeBaseFolder()
        {
            // Source = the parent of everything (for example the Documents folder that holds the base folder).
            Put("setup.msi", "abc");
            var baseFolder = Path.Combine(_root, "base");
            Directory.CreateDirectory(baseFolder);
            var target = Path.Combine(baseFolder, "proj", "versions", "1.0", "source");
            File.WriteAllText(Path.Combine(_root, "setup.msi"), "root level installer");

            Assert.Equal(ImportProblem.TargetInsideSource, ProblemOf(() => SourceImporter.ImportFolder(_root, "setup.msi", target)));

            Assert.False(Directory.Exists(target));
        }

        [Fact]
        public void ImportFolder_RejectsASourceThatLiesInsideTheTarget()
        {
            Directory.CreateDirectory(_target);
            var inside = Path.Combine(_target, "vendor");
            Directory.CreateDirectory(inside);
            File.WriteAllText(Path.Combine(inside, "setup.msi"), "abc");

            Assert.Equal(ImportProblem.SourceInsideTarget, ProblemOf(() => SourceImporter.ImportFolder(inside, "setup.msi", _target)));

            Assert.True(File.Exists(Path.Combine(inside, "setup.msi")));
        }

        [Fact]
        public void ImportFolder_RejectsTheSameFolderAsSourceAndTarget()
        {
            Put("setup.msi", "abc");

            Assert.Equal(ImportProblem.TargetInsideSource, ProblemOf(() => SourceImporter.ImportFolder(_vendor, "setup.msi", _vendor)));
        }

        [Fact]
        public void ImportFolder_ComparesPathsIgnoringCaseAndTrailingSeparators()
        {
            Put("setup.msi", "abc");
            var target = Path.Combine(_vendor, "project", "source");

            Assert.Equal(ImportProblem.TargetInsideSource,
                ProblemOf(() => SourceImporter.ImportFolder(_vendor.ToUpperInvariant() + "\\", "setup.msi", target.ToLowerInvariant())));
        }

        [Fact]
        public void ImportFolder_RejectsAJunctionInsideTheSourceBeforeCopyingAnything()
        {
            Put("setup.msi", "abc");
            var outside = Path.Combine(_root, "outside");
            Directory.CreateDirectory(outside);
            File.WriteAllText(Path.Combine(outside, "secret.txt"), "secret");
            var link = Path.Combine(_vendor, "link");
            MakeJunction(link, outside);
            try
            {
                var ex = Assert.Throws<ImportRejectedException>(() => SourceImporter.ImportFolder(_vendor, "setup.msi", _target));

                Assert.Equal(ImportProblem.ReparsePointFound, ex.Problem);
                Assert.Equal(link, ex.Path);
                Assert.False(Directory.Exists(_target));
                Assert.Equal("secret", File.ReadAllText(Path.Combine(outside, "secret.txt")));
            }
            finally
            {
                Directory.Delete(link);
            }
        }

        [Fact]
        public void ImportFolder_RejectsAJunctionAsTheSourceFolderItself()
        {
            var real = Path.Combine(_root, "real");
            Directory.CreateDirectory(real);
            File.WriteAllText(Path.Combine(real, "setup.msi"), "abc");
            var link = Path.Combine(_root, "linked-vendor");
            MakeJunction(link, real);
            try
            {
                Assert.Equal(ImportProblem.ReparsePointFound, ProblemOf(() => SourceImporter.ImportFolder(link, "setup.msi", _target)));
                Assert.False(Directory.Exists(_target));
            }
            finally
            {
                Directory.Delete(link);
            }
        }

        [Fact]
        public void ImportFolder_RejectsATargetThatWouldExceedTheWindowsPathLimitBeforeCopying()
        {
            var deep = string.Join("/", Enumerable.Repeat(new string('d', 20), 5)) + "/setup.msi";
            Put(deep, "abc");
            var longTarget = Path.Combine(_root, new string('t', 150), "versions", "1.0", "source");

            var ex = Assert.Throws<ImportRejectedException>(() => SourceImporter.ImportFolder(_vendor, deep, longTarget));

            Assert.Equal(ImportProblem.PathTooLong, ex.Problem);
            Assert.True(ex.Path.Length > SourceImporter.MaxPathLength);
            Assert.False(Directory.Exists(Path.Combine(_root, new string('t', 150))));
        }

        [Fact]
        public void ImportFolder_AcceptsPathsJustBelowTheLimit()
        {
            var deep = string.Join("/", Enumerable.Repeat(new string('d', 20), 3)) + "/setup.msi";
            Put(deep, "abc");

            var result = SourceImporter.ImportFolder(_vendor, deep, _target);

            Assert.Single(result.Manifest.Files);
        }

        // ---- Failure handling ----

        [Fact]
        public void ImportFolder_CleansUpItsOwnStagingFolderWhenCopyingFails()
        {
            Put("setup.msi", "abc");
            var locked = Put("locked.dat", "in use");
            var before = SourceManifestBuilder.Build(_vendor);

            using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                Assert.Throws<IOException>(() => SourceImporter.ImportFolder(_vendor, "setup.msi", _target));
            }

            var parent = Path.GetDirectoryName(_target);
            Assert.False(Directory.Exists(_target));
            Assert.Empty(Directory.Exists(parent) ? Directory.GetDirectories(parent) : new string[0]);
            Assert.Empty(SourceManifestBuilder.Compare(_vendor, before));
        }

        [Fact]
        public void ImportFolder_NeverDeletesAnythingInAnExistingTargetWhenCopyingFails()
        {
            Put("setup.msi", "abc");
            var locked = Put("locked.dat", "in use");
            Directory.CreateDirectory(_target);

            using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                Assert.Throws<IOException>(() => SourceImporter.ImportFolder(_vendor, "setup.msi", _target));
            }

            Assert.True(Directory.Exists(_target));
            Assert.Empty(Directory.GetFileSystemEntries(_target));
        }

        // ---- VersionStore.ImportSource ----

        [Fact]
        public void VersionStore_StoresTheSourceAndWritesTheManifest()
        {
            var projects = new ProjectStore(Path.Combine(_root, "base"));
            projects.Create("contoso", "Contoso");
            var versions = new VersionStore(projects);
            versions.Create("contoso", NewConfig("1.0"));
            var msi = Put("setup.msi", "abc");

            var result = versions.ImportSource("contoso", "1.0", SourceImporter.Classify(new[] { msi }, false), null);

            Assert.Equal("setup.msi", result.InstallerRelativePath);
            Assert.True(File.Exists(Path.Combine(versions.SourceDirectory("contoso", "1.0"), "setup.msi")));
            var manifest = SourceManifestStore.Read(versions.SourceManifestPath("contoso", "1.0"));
            Assert.Equal("setup.msi", manifest.Files.Single().Path);
            Assert.Empty(SourceManifestBuilder.Compare(versions.SourceDirectory("contoso", "1.0"), manifest));
        }

        [Fact]
        public void VersionStore_NeverReplacesAStoredSource()
        {
            var projects = new ProjectStore(Path.Combine(_root, "base"));
            projects.Create("contoso", "Contoso");
            var versions = new VersionStore(projects);
            versions.Create("contoso", NewConfig("1.0"));
            var msi = Put("setup.msi", "abc");
            versions.ImportSource("contoso", "1.0", SourceImporter.Classify(new[] { msi }, false), null);
            var manifestBefore = File.ReadAllText(versions.SourceManifestPath("contoso", "1.0"));
            var other = Put("other.msi", "different");

            Assert.Equal(ImportProblem.SourceAlreadyStored,
                ProblemOf(() => versions.ImportSource("contoso", "1.0", SourceImporter.Classify(new[] { other }, false), null)));

            Assert.Equal(manifestBefore, File.ReadAllText(versions.SourceManifestPath("contoso", "1.0")));
            Assert.Equal("abc", File.ReadAllText(Path.Combine(versions.SourceDirectory("contoso", "1.0"), "setup.msi")));
        }

        [Fact]
        public void VersionStore_ImportIsRefusedWhileAnotherHolderHasTheVersionLock()
        {
            var projects = new ProjectStore(Path.Combine(_root, "base"));
            projects.Create("contoso", "Contoso");
            var versions = new VersionStore(projects);
            versions.Create("contoso", NewConfig("1.0"));
            var msi = Put("setup.msi", "abc");

            using (IntunePackageBuilder.Core.Storage.VersionLock.Acquire(versions.VersionDirectory("contoso", "1.0")))
            {
                Assert.Throws<IntunePackageBuilder.Core.Storage.LockHeldException>(
                    () => versions.ImportSource("contoso", "1.0", SourceImporter.Classify(new[] { msi }, false), null));
            }

            Assert.False(Directory.Exists(versions.SourceDirectory("contoso", "1.0")));
        }

        [Fact]
        public void VersionStore_ImportsAFolder()
        {
            var projects = new ProjectStore(Path.Combine(_root, "base"));
            projects.Create("contoso", "Contoso");
            var versions = new VersionStore(projects);
            versions.Create("contoso", NewConfig("1.0"));
            Put("setup.msi", "abc");
            Put("data1.cab", "cab");

            var result = versions.ImportSource("contoso", "1.0", SourceImporter.Classify(new[] { _vendor }, true), "setup.msi");

            Assert.Equal(2, result.Manifest.Files.Count);
            Assert.Equal(2, SourceManifestStore.Read(versions.SourceManifestPath("contoso", "1.0")).Files.Count);
        }

        private static PackageVersionConfig NewConfig(string version)
        {
            var config = PackageVersionConfig.CreateDefault("contoso", InstallerType.Msi);
            config.Identity.TargetVersion = version;
            return config;
        }

        private static void MakeJunction(string link, string target)
        {
            var info = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c mklink /J \"" + link + "\" \"" + target + "\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using (var process = Process.Start(info))
            {
                var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
                process.WaitForExit();
                Assert.True(process.ExitCode == 0, "mklink failed: " + output);
            }
        }
    }
}
