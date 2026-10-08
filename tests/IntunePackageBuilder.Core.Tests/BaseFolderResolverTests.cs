using System;
using System.IO;
using System.Linq;
using IntunePackageBuilder.Core.Settings;
using Xunit;

namespace IntunePackageBuilder.Core.Tests
{
    public sealed class BaseFolderResolverTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "ipb-base-" + Guid.NewGuid().ToString("N"));

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

        [Fact]
        public void NotConfigured_SuggestsTheDocumentsFolderWithoutCreatingIt()
        {
            var resolution = BaseFolderResolver.Resolve(new AppSettings());

            Assert.Equal(BaseFolderStatus.NotConfigured, resolution.Status);
            Assert.Equal(BaseFolderResolver.SuggestDefault(), resolution.Path);
            Assert.EndsWith("Intune-Paketprojekte", resolution.Path);
            Assert.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), resolution.Path);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void EmptyValuesCountAsNotConfigured(string value)
        {
            Assert.Equal(BaseFolderStatus.NotConfigured, BaseFolderResolver.Resolve(new AppSettings { BaseFolder = value }).Status);
        }

        [Fact]
        public void AnExistingWritableFolderIsOk()
        {
            Directory.CreateDirectory(_root);

            var resolution = BaseFolderResolver.Resolve(new AppSettings { BaseFolder = _root });

            Assert.Equal(BaseFolderStatus.Ok, resolution.Status);
            Assert.Equal(_root, resolution.Path);
        }

        [Fact]
        public void TheWriteProbeLeavesNothingBehind()
        {
            Directory.CreateDirectory(_root);

            Assert.True(BaseFolderResolver.CanWrite(_root));

            Assert.Empty(Directory.GetFileSystemEntries(_root));
        }

        [Fact]
        public void AMissingConfiguredFolderIsUnreachableAndNeverReplaced()
        {
            var missing = Path.Combine(_root, "offline-share");

            var resolution = BaseFolderResolver.Resolve(new AppSettings { BaseFolder = missing });

            Assert.Equal(BaseFolderStatus.Unreachable, resolution.Status);
            Assert.Equal(missing, resolution.Path);
            Assert.NotEqual(BaseFolderResolver.SuggestDefault(), resolution.Path);
            Assert.False(Directory.Exists(missing));
        }

        [Fact]
        public void AFileInsteadOfAFolderIsUnreachable()
        {
            File.WriteAllText(_root, "a file");

            Assert.Equal(BaseFolderStatus.Unreachable, BaseFolderResolver.Resolve(new AppSettings { BaseFolder = _root }).Status);
        }

        [Theory]
        [InlineData("relative\\folder")]
        [InlineData("projects")]
        public void ARelativePathIsNotAcceptedAndKeptAsConfigured(string relative)
        {
            var resolution = BaseFolderResolver.Resolve(new AppSettings { BaseFolder = relative });

            Assert.Equal(BaseFolderStatus.Unreachable, resolution.Status);
            Assert.Equal(relative, resolution.Path);
        }

        [Fact]
        public void AnExistingFolderWithoutWriteAccessIsReportedAsNotWritable()
        {
            Directory.CreateDirectory(_root);

            var resolution = BaseFolderResolver.Resolve(new AppSettings { BaseFolder = _root }, directory => false);

            Assert.Equal(BaseFolderStatus.NotWritable, resolution.Status);
            Assert.Equal(_root, resolution.Path);
        }

        [Fact]
        public void CanWrite_IsFalseWhenTheProbeFileCannotBeCreated()
        {
            // A path below a file cannot be a directory, so creating the probe file fails.
            File.WriteAllText(_root, "a file");

            Assert.False(BaseFolderResolver.CanWrite(Path.Combine(_root, "below")));
        }
    }
}
