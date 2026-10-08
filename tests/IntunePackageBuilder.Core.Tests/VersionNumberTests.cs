using System.Collections.Generic;
using IntunePackageBuilder.Core.Versions;
using Xunit;

namespace IntunePackageBuilder.Core.Tests
{
    public class VersionNumberTests
    {
        private static VersionNumber Parse(string text)
        {
            VersionNumber result;
            Assert.True(VersionNumber.TryParse(text, out result), "Could not parse '" + text + "'");
            return result;
        }

        [Theory]
        [InlineData("1")]
        [InlineData("1.0")]
        [InlineData("1.2.3")]
        [InlineData("10.0.19045.5011")]
        [InlineData(" 2.1 ")]
        public void TryParse_AcceptsNumericVersions(string text)
        {
            VersionNumber result;
            Assert.True(VersionNumber.TryParse(text, out result));
            Assert.Equal(text.Trim(), result.Text);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("1..2")]
        [InlineData("1.2.")]
        [InlineData(".1")]
        [InlineData("1.2.3.4.5")]
        [InlineData("a.b")]
        [InlineData("1.x")]
        [InlineData("-1")]
        [InlineData("1.2-beta")]
        [InlineData("1234567890")]
        public void TryParse_RejectsOtherText(string text)
        {
            VersionNumber result;
            Assert.False(VersionNumber.TryParse(text, out result));
            Assert.Null(result);
        }

        [Fact]
        public void CompareTo_IsNumericNotTextual()
        {
            Assert.True(Parse("1.10").CompareTo(Parse("1.9")) > 0);
            Assert.True(Parse("2.0").CompareTo(Parse("10.0")) < 0);
            Assert.True(Parse("1.0.0.1").CompareTo(Parse("1.0")) > 0);
        }

        [Fact]
        public void CompareTo_IgnoresTrailingZeroParts()
        {
            Assert.Equal(0, Parse("1.0").CompareTo(Parse("1.0.0")));
            Assert.True(Parse("1.0").Equals(Parse("1.0.0.0")));
            Assert.Equal(Parse("1.0").GetHashCode(), Parse("1.0.0").GetHashCode());
        }

        [Fact]
        public void Sorting_OrdersVersionsNumerically()
        {
            var versions = new List<VersionNumber> { Parse("1.10.0"), Parse("1.2.0"), Parse("1.9.5"), Parse("0.9") };
            versions.Sort();

            Assert.Equal(new[] { "0.9", "1.2.0", "1.9.5", "1.10.0" }, versions.ConvertAll(v => v.Text));
        }
    }
}
