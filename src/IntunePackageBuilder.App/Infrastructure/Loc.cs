using System;
using System.Globalization;
using System.Resources;

namespace IntunePackageBuilder.App.Infrastructure
{
    /// <summary>
    /// Access to the user-visible texts. They come from <c>Resources/Strings.resx</c> (English) and
    /// <c>Strings.de.resx</c> (German); the program language decides (the UI language of Windows unless one is set).
    /// No text is written into code or XAML.
    /// </summary>
    public static class Loc
    {
        private static readonly ResourceManager Manager =
            new ResourceManager("IntunePackageBuilder.App.Resources.Strings", typeof(Loc).Assembly);

        private static CultureInfo _culture = CultureInfo.CurrentUICulture;

        public static CultureInfo Culture
        {
            get { return _culture; }
        }

        /// <summary><c>de</c> or <c>en</c>: the language of the generated guide and the notices on the device.</summary>
        public static string Language
        {
            get { return string.Equals(_culture.TwoLetterISOLanguageName, "de", StringComparison.OrdinalIgnoreCase) ? "de" : "en"; }
        }

        public static void Use(CultureInfo culture)
        {
            _culture = culture ?? CultureInfo.CurrentUICulture;
        }

        /// <summary>The text for a key. A missing key is shown as <c>!key!</c> so it is noticed (a test checks every key that code uses).</summary>
        public static string Get(string key)
        {
            return Manager.GetString(key, _culture) ?? "!" + key + "!";
        }

        public static string Format(string key, params object[] arguments)
        {
            var text = Get(key);
            return arguments == null || arguments.Length == 0 ? text : string.Format(_culture, text, arguments);
        }
    }
}
