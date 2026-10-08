using IntunePackageBuilder.Core;
using Xunit;

namespace IntunePackageBuilder.Core.Tests
{
    public class AppInfoTests
    {
        [Fact]
        public void ProductName_IsSet()
        {
            Assert.Equal("Intune Package Builder", AppInfo.ProductName);
        }

        [Fact]
        public void Version_HasThreeParts()
        {
            Assert.Equal(3, AppInfo.Version.Split('.').Length);
        }
    }
}
