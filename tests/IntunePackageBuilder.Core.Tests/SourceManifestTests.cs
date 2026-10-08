using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using IntunePackageBuilder.Core.Sources;
using IntunePackageBuilder.Core.Storage;
using Newtonsoft.Json.Linq;
using Xunit;

namespace IntunePackageBuilder.Core.Tests
{
    public sealed class SourceManifestTests : IDisposable
    {
        private const string AbcSha256 = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";
        private const string EmptySha256 = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

        private readonly string _root = Path.Combine(Path.GetTempPath(), "ipb-source-" + Guid.NewGuid().ToString("N"));
        private readonly string _source;

        public SourceManifestTests()
        {
            _source = Path.Combine(_root, "source");
            Directory.CreateDirectory(_source);
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
            var path = Path.Combine(_source, relative.Replace('/', '\\'));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, new UTF8Encoding(false).GetBytes(content));
            return path;
        }

        [Fact]
        public void Build_ListsAllFilesSortedWithSizeAndSha256()
        {
            Put("setup.msi", "abc");
            Put("vendor/lib/b.dll", "");
            Put("vendor/a.txt", "abc");

            var manifest = SourceManifestBuilder.Build(_source);

            Assert.Equal(new[] { "setup.msi", "vendor/a.txt", "vendor/lib/b.dll" }, manifest.Files.Select(f => f.Path).ToArray());
            Assert.Equal(3, manifest.Files[0].Size);
            Assert.Equal(AbcSha256, manifest.Files[0].Sha256);
            Assert.Equal(AbcSha256, manifest.Files[1].Sha256);
            Assert.Equal(0, manifest.Files[2].Size);
            Assert.Equal(EmptySha256, manifest.Files[2].Sha256);
            Assert.Equal(SourceManifest.CurrentSchemaVersion, manifest.SchemaVersion);
        }

        [Fact]
        public void Build_OfAnEmptyFolderHasNoFiles()
        {
            Assert.Empty(SourceManifestBuilder.Build(_source).Files);
        }

        [Fact]
        public void Build_RequiresAnExistingFolder()
        {
            Assert.Throws<DirectoryNotFoundException>(() => SourceManifestBuilder.Build(Path.Combine(_root, "missing")));
        }

        [Fact]
        public void Build_HandlesUnicodeFileNames()
        {
            Put("Anleitung \u00e4\u00f6\u00fc.txt", "abc");

            var manifest = SourceManifestBuilder.Build(_source);

            Assert.Equal("Anleitung \u00e4\u00f6\u00fc.txt", manifest.Files.Single().Path);
            Assert.Empty(SourceManifestBuilder.Compare(_source, manifest));
        }

        [Fact]
        public void Compare_ReportsNothingForAnUnchangedSource()
        {
            Put("setup.msi", "abc");
            Put("vendor/a.txt", "data");
            var manifest = SourceManifestBuilder.Build(_source);

            Assert.Empty(SourceManifestBuilder.Compare(_source, manifest));
            SourceManifestBuilder.RequireUnchanged(_source, manifest);
        }

        [Fact]
        public void Compare_DetectsAChangeWithTheSameSize()
        {
            Put("setup.msi", "abc");
            var manifest = SourceManifestBuilder.Build(_source);

            Put("setup.msi", "abd");

            var difference = Assert.Single(SourceManifestBuilder.Compare(_source, manifest));
            Assert.Equal("setup.msi", difference.Path);
            Assert.Equal(SourceDifferenceKind.Modified, difference.Kind);
        }

        [Fact]
        public void Compare_DetectsAChangeOfSize()
        {
            Put("setup.msi", "abc");
            var manifest = SourceManifestBuilder.Build(_source);

            Put("setup.msi", "abcdef");

            Assert.Equal(SourceDifferenceKind.Modified, Assert.Single(SourceManifestBuilder.Compare(_source, manifest)).Kind);
        }

        [Fact]
        public void Compare_DetectsMissingAndAddedFiles()
        {
            Put("setup.msi", "abc");
            Put("old.txt", "old");
            var manifest = SourceManifestBuilder.Build(_source);

            File.Delete(Path.Combine(_source, "old.txt"));
            Put("sub/new.txt", "new");

            var differences = SourceManifestBuilder.Compare(_source, manifest);

            Assert.Equal(new[] { "Missing: old.txt", "Added: sub/new.txt" }, differences.Select(d => d.ToString()).ToArray());
        }

        [Fact]
        public void Compare_TreatsARenameAsMissingPlusAdded()
        {
            Put("setup.msi", "abc");
            var manifest = SourceManifestBuilder.Build(_source);

            File.Move(Path.Combine(_source, "setup.msi"), Path.Combine(_source, "renamed.msi"));

            Assert.Equal(new[] { "Added: renamed.msi", "Missing: setup.msi" },
                SourceManifestBuilder.Compare(_source, manifest).Select(d => d.ToString()).ToArray());
        }

        [Fact]
        public void RequireUnchanged_ThrowsAndListsTheDifferences()
        {
            Put("setup.msi", "abc");
            var manifest = SourceManifestBuilder.Build(_source);
            Put("setup.msi", "changed");

            var ex = Assert.Throws<SourceChangedException>(() => SourceManifestBuilder.RequireUnchanged(_source, manifest));

            Assert.Equal("setup.msi", Assert.Single(ex.Differences).Path);
        }

        [Fact]
        public void Build_RefusesToFollowAJunction()
        {
            var outside = Path.Combine(_root, "outside");
            Directory.CreateDirectory(outside);
            File.WriteAllText(Path.Combine(outside, "secret.txt"), "x");
            var link = Path.Combine(_source, "link");
            MakeJunction(link, outside);
            try
            {
                var ex = Assert.Throws<UnsafeSourceException>(() => SourceManifestBuilder.Build(_source));

                Assert.Equal(link, ex.Path);
                Assert.True(File.Exists(Path.Combine(outside, "secret.txt")), "Nothing outside the source may be touched.");
            }
            finally
            {
                // Remove the link itself (not its target) before the folder is deleted.
                Directory.Delete(link);
            }
        }

        [Fact]
        public void Store_WritesAndReadsTheManifest()
        {
            Put("setup.msi", "abc");
            var manifest = SourceManifestBuilder.Build(_source);
            var file = Path.Combine(_root, "source-manifest.json");

            SourceManifestStore.WriteNew(file, manifest);
            var loaded = SourceManifestStore.Read(file);

            Assert.Equal(manifest.CreatedUtc.Ticks, loaded.CreatedUtc.Ticks);
            Assert.Equal("setup.msi", loaded.Files.Single().Path);
            Assert.Equal(AbcSha256, loaded.Files.Single().Sha256);
            Assert.Empty(SourceManifestBuilder.Compare(_source, loaded));
        }

        [Fact]
        public void Store_UsesCamelCaseNames()
        {
            Put("setup.msi", "abc");
            var file = Path.Combine(_root, "source-manifest.json");
            SourceManifestStore.WriteNew(file, SourceManifestBuilder.Build(_source));

            var document = JObject.Parse(File.ReadAllText(file));
            var entry = (JObject)document["files"][0];

            Assert.Equal(1, document.Value<int>("schemaVersion"));
            Assert.Equal("setup.msi", entry.Value<string>("path"));
            Assert.Equal(3, entry.Value<int>("size"));
            Assert.Equal(AbcSha256, entry.Value<string>("sha256"));
        }

        [Fact]
        public void Store_NeverReplacesAnExistingManifest()
        {
            Put("setup.msi", "abc");
            var file = Path.Combine(_root, "source-manifest.json");
            SourceManifestStore.WriteNew(file, SourceManifestBuilder.Build(_source));
            var before = File.ReadAllText(file);
            Put("setup.msi", "changed");

            Assert.Throws<IOException>(() => SourceManifestStore.WriteNew(file, SourceManifestBuilder.Build(_source)));

            Assert.Equal(before, File.ReadAllText(file));
        }

        [Fact]
        public void Store_RejectsANewerSchema()
        {
            var file = Path.Combine(_root, "source-manifest.json");
            File.WriteAllText(file, "{\"schemaVersion\":99,\"files\":[]}");

            Assert.Throws<UnsupportedSchemaException>(() => SourceManifestStore.Read(file));
        }

        [Fact]
        public void Store_RejectsAnEntryWithoutAChecksum()
        {
            var file = Path.Combine(_root, "source-manifest.json");
            File.WriteAllText(file, "{\"schemaVersion\":1,\"files\":[{\"path\":\"setup.msi\",\"size\":3}]}");

            Assert.Throws<StorageFormatException>(() => SourceManifestStore.Read(file));
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
