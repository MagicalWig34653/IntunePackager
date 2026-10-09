using System.IO;
using IntunePackageBuilder.Build.Packaging;
using Xunit;

namespace IntunePackageBuilder.Build.Tests
{
    public class IntunewinVerifierTests
    {
        [Fact]
        public void AValidContainerIsAccepted()
        {
            using (var directory = new TempDirectory())
            {
                var path = directory.Combine("a.intunewin");
                TestPackages.WriteIntunewin(path);

                IntunewinVerifier.Verify(path);
            }
        }

        [Fact]
        public void AMissingFileIsReported()
        {
            using (var directory = new TempDirectory())
            {
                var exception = Assert.Throws<ContentPrepException>(() => IntunewinVerifier.Verify(directory.Combine("missing.intunewin")));

                Assert.Equal(ContentPrepProblem.OutputMissing, exception.Problem);
            }
        }

        [Fact]
        public void AnEmptyFileIsInvalid()
        {
            using (var directory = new TempDirectory())
            {
                var path = directory.Combine("empty.intunewin");
                File.WriteAllBytes(path, new byte[0]);

                Assert.Equal(ContentPrepProblem.OutputInvalid, Assert.Throws<ContentPrepException>(() => IntunewinVerifier.Verify(path)).Problem);
            }
        }

        [Fact]
        public void AFileThatIsNotAZipIsInvalid()
        {
            using (var directory = new TempDirectory())
            {
                var path = directory.Combine("text.intunewin");
                File.WriteAllText(path, "this is not a zip container");

                Assert.Equal(ContentPrepProblem.OutputInvalid, Assert.Throws<ContentPrepException>(() => IntunewinVerifier.Verify(path)).Problem);
            }
        }

        [Theory]
        [InlineData(false, true, false)]
        [InlineData(true, false, false)]
        [InlineData(true, true, true)]
        public void AContainerWithoutContentMetadataOrWithEmptyContentIsInvalid(bool withContent, bool withMetadata, bool emptyContent)
        {
            using (var directory = new TempDirectory())
            {
                var path = directory.Combine("partial.intunewin");
                TestPackages.WriteIntunewin(path, withContent, withMetadata, emptyContent);

                Assert.Equal(ContentPrepProblem.OutputInvalid, Assert.Throws<ContentPrepException>(() => IntunewinVerifier.Verify(path)).Problem);
            }
        }
    }
}
