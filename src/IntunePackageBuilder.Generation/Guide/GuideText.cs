using System;
using System.Globalization;
using System.Reflection;
using System.Resources;

namespace IntunePackageBuilder.Generation.Guide
{
    /// <summary>
    /// Texts of the generated guide. They come from the resources <c>Resources/GuideStrings.resx</c> (English) and
    /// <c>GuideStrings.de.resx</c> (German); the language is the one recorded in the build snapshot, not the language
    /// of the machine that renders the guide.
    /// </summary>
    public static class GuideText
    {
        public const string English = "en";
        public const string German = "de";

        private static readonly ResourceManager Manager =
            new ResourceManager("IntunePackageBuilder.Generation.Resources.GuideStrings", typeof(GuideText).Assembly);

        public static CultureInfo CultureFor(string language)
        {
            return string.Equals(language, German, StringComparison.OrdinalIgnoreCase)
                ? CultureInfo.GetCultureInfo(German)
                : CultureInfo.InvariantCulture;
        }

        /// <summary>The text for <paramref name="key"/>. A missing key is a programming error and throws.</summary>
        public static string Get(string language, string key)
        {
            var text = Manager.GetString(key, CultureFor(language));
            if (text == null)
            {
                throw new MissingManifestResourceException("Missing guide text: " + key);
            }

            return text;
        }

        public static string Format(string language, string key, params object[] arguments)
        {
            return string.Format(CultureInfo.InvariantCulture, Get(language, key), arguments);
        }
    }
}
