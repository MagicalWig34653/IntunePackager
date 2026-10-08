using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace IntunePackageBuilder.Core.Builds
{
    /// <summary>
    /// Identifies exactly one package build: <c>yyyyMMdd-HHmmss-xxxx</c> (UTC time plus four random hexadecimal
    /// characters). It names a folder, so it is file-system safe. A new build ID is never a reason to install the
    /// same software again: the software version describes the target state, the build ID only the build (SPEC 6.4).
    /// </summary>
    public static class BuildId
    {
        private static readonly Regex Pattern = new Regex(@"^\d{8}-\d{6}-[0-9a-f]{4}$", RegexOptions.CultureInvariant);
        private static readonly Random Random = new Random();

        public static string New(DateTime utcNow)
        {
            int suffix;
            lock (Random)
            {
                suffix = Random.Next(0, 0x10000);
            }

            return utcNow.ToUniversalTime().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)
                + "-" + suffix.ToString("x4", CultureInfo.InvariantCulture);
        }

        public static bool IsValid(string value)
        {
            return value != null && Pattern.IsMatch(value);
        }
    }
}
