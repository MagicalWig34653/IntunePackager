using System;
using System.IO;
using System.Linq;
using IntunePackageBuilder.Core.Storage;
using Xunit;

namespace IntunePackageBuilder.Core.Tests
{
    public sealed class AtomicFileTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "ipb-atomic-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, true);
            }
        }

        [Fact]
        public void WriteAllText_CreatesFileAndParentDirectories()
        {
            var path = Path.Combine(_root, "a", "b", "data.json");

            AtomicFile.WriteAllText(path, "{}");

            Assert.Equal("{}", File.ReadAllText(path));
        }

        [Fact]
        public void WriteAllText_ReplacesExistingFile()
        {
            var path = Path.Combine(_root, "data.json");
            AtomicFile.WriteAllText(path, "old");

            AtomicFile.WriteAllText(path, "new");

            Assert.Equal("new", File.ReadAllText(path));
        }

        [Fact]
        public void WriteAllText_LeavesNoTemporaryFiles()
        {
            var path = Path.Combine(_root, "data.json");
            AtomicFile.WriteAllText(path, "one");
            AtomicFile.WriteAllText(path, "two");

            var files = Directory.GetFiles(_root).Select(Path.GetFileName).ToArray();

            Assert.Equal(new[] { "data.json" }, files);
        }

        [Fact]
        public void WriteAllText_WritesUtf8WithoutBom()
        {
            var path = Path.Combine(_root, "data.json");

            AtomicFile.WriteAllText(path, "\u00e4");

            var bytes = File.ReadAllBytes(path);
            Assert.Equal(new byte[] { 0xC3, 0xA4 }, bytes);
        }

        [Fact]
        public void WriteAllText_KeepsOriginalAndCleansUpWhenTargetIsLocked()
        {
            var path = Path.Combine(_root, "data.json");
            AtomicFile.WriteAllText(path, "original");

            using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                Assert.Throws<IOException>(() => AtomicFile.WriteAllText(path, "replacement"));
            }

            Assert.Equal("original", File.ReadAllText(path));
            Assert.Equal(new[] { "data.json" }, Directory.GetFiles(_root).Select(Path.GetFileName).ToArray());
        }
    }
}
