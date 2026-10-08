using System.Reflection;

namespace IntunePackageBuilder.Core
{
    /// <summary>Product name and version of the authoring tool.</summary>
    public static class AppInfo
    {
        public const string ProductName = "Intune Package Builder";

        public static string Version
        {
            get
            {
                var v = typeof(AppInfo).Assembly.GetName().Version;
                return v.Major + "." + v.Minor + "." + v.Build;
            }
        }
    }
}
