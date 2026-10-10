using System;
using System.Globalization;
using System.Linq;
using System.Text;
using IntunePackageBuilder.Core.Builds;
using IntunePackageBuilder.Core.Storage;
using IntunePackageBuilder.Core.Versions;
using IntunePackageBuilder.Generation.Guide;
using IntunePackageBuilder.Generation.Intune;
using IntunePackageBuilder.Generation.Scripts;

namespace IntunePackageBuilder.Generation.Psadt
{
    /// <summary>
    /// The files that configure a PSAppDeployToolkit package without touching the toolkit itself: a partial
    /// <c>Config\config.psd1</c> and partial <c>Strings\strings.psd1</c> files. The toolkit merges them over its own
    /// defaults (values that are empty in the overlay keep the default), so the released module files stay unchanged
    /// and its integrity check keeps passing. Everything comes from the snapshot and the Intune settings, like the guide.
    /// </summary>
    public static class PsadtOverlay
    {
        public const string ConfigFolder = "Config";
        public const string ConfigFile = "config.psd1";
        public const string StringsFolder = "Strings";
        public const string StringsFile = "strings.psd1";
        public const string AssetsFolder = "Assets";

        /// <summary>The toolkit's own limit of the interaction dialogs; the dialogs end with the retry code when it is reached.</summary>
        public static int DialogTimeoutSeconds(int timeoutMinutes)
        {
            // Same share of the Intune time limit as the native wrapper waits for programs to close: a third, at most 30 minutes.
            return Math.Max(1, Math.Min(30, timeoutMinutes / 3)) * 60;
        }

        public static string BuildConfig(BuildSnapshot snapshot, IntuneSettings settings)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException("snapshot");
            }

            if (settings == null)
            {
                throw new ArgumentNullException("settings");
            }

            var config = snapshot.Configuration;
            var psadt = config.Deployment.Psadt;
            var retry = settings.ReturnCodes.Where(e => e.Type == ReturnCodeType.Retry).Select(e => e.Code).DefaultIfEmpty(1618).First();
            var company = string.IsNullOrWhiteSpace(psadt.CompanyName) ? settings.App.Publisher : psadt.CompanyName.Trim();
            var builder = new StringBuilder();
            builder.Append("@{\r\n");

            if (!string.IsNullOrWhiteSpace(psadt.LogoFile) || !string.IsNullOrWhiteSpace(psadt.LogoDarkFile) || !string.IsNullOrWhiteSpace(psadt.BannerFile))
            {
                builder.Append("    Assets = @{\r\n");
                AppendAsset(builder, "Logo", psadt.LogoFile);
                AppendAsset(builder, "LogoDark", psadt.LogoDarkFile);
                AppendAsset(builder, "Banner", psadt.BannerFile);
                builder.Append("    }\r\n");
            }

            builder.Append("    Toolkit = @{\r\n");
            builder.Append("        CompanyName = ").Append(ExpandableLiteral(company, "companyName")).Append("\r\n");
            builder.Append("        LogPath = ").Append(ExpandableLiteral(settings.Logs.Directory, "logPath")).Append("\r\n");
            builder.Append("    }\r\n");

            builder.Append("    UI = @{\r\n");
            builder.Append("        BalloonNotifications = ").Append(psadt.BalloonNotifications ? "$true" : "$false").Append("\r\n");
            builder.Append("        DialogStyle = '").Append(psadt.DialogStyle.ToString()).Append("'\r\n");
            if (!string.IsNullOrWhiteSpace(psadt.AccentColor))
            {
                builder.Append("        FluentAccentColor = ").Append(AccentLiteral(psadt.AccentColor)).Append("\r\n");
            }

            builder.Append("        DefaultExitCode = ").Append(retry.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
            builder.Append("        DeferExitCode = ").Append(retry.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
            builder.Append("        DefaultTimeout = ").Append(DialogTimeoutSeconds(settings.Program.TimeoutMinutes).ToString(CultureInfo.InvariantCulture)).Append("\r\n");
            var language = UiLanguage(psadt, snapshot.Language);
            if (language != null)
            {
                builder.Append("        LanguageOverride = ").Append(ExpandableLiteral(language, "uiLanguage")).Append("\r\n");
            }

            builder.Append("    }\r\n");
            builder.Append("}\r\n");
            return builder.ToString();
        }

        /// <summary>
        /// Strings file for one language (<c>en</c> is also the neutral file). The close-programs texts say that the user closes
        /// the programs: the toolkit's own default text announces an automatic close that this package never does.
        /// </summary>
        public static string BuildStrings(BuildSnapshot snapshot, string language)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException("snapshot");
            }

            var interaction = snapshot.Configuration.Interaction;
            var builder = new StringBuilder();
            builder.Append("@{\r\n");
            builder.Append("    CloseAppsPrompt = @{\r\n");
            builder.Append("        Classic = @{\r\n");
            builder.Append("            CloseAppsMessage = @{\r\n");
            AppendPair(builder, "                ", "Install", GuideText.Get(language, "Psadt_CloseInstall"), "closeInstall");
            AppendPair(builder, "                ", "Uninstall", GuideText.Get(language, "Psadt_CloseUninstall"), "closeUninstall");
            builder.Append("            }\r\n");
            builder.Append("        }\r\n");
            builder.Append("        Fluent = @{\r\n");
            builder.Append("            DialogMessage = @{\r\n");
            AppendPair(builder, "                ", "Install", GuideText.Get(language, "Psadt_CloseInstall"), "closeInstall");
            AppendPair(builder, "                ", "Uninstall", GuideText.Get(language, "Psadt_CloseUninstall"), "closeUninstall");
            builder.Append("            }\r\n");
            builder.Append("        }\r\n");
            if (!string.IsNullOrWhiteSpace(interaction.DetailMessage))
            {
                builder.Append("        CustomMessage = ").Append(StringsLiteral(interaction.DetailMessage.Trim(), "detailMessage")).Append("\r\n");
            }

            builder.Append("    }\r\n");

            if (!string.IsNullOrWhiteSpace(interaction.InstallMessage) || !string.IsNullOrWhiteSpace(interaction.UninstallMessage))
            {
                builder.Append("    ProgressPrompt = @{\r\n");
                builder.Append("        Message = @{\r\n");
                if (!string.IsNullOrWhiteSpace(interaction.InstallMessage))
                {
                    builder.Append("            Install = ").Append(StringsLiteral(interaction.InstallMessage.Trim(), "installMessage")).Append("\r\n");
                }

                if (!string.IsNullOrWhiteSpace(interaction.UninstallMessage))
                {
                    builder.Append("            Uninstall = ").Append(StringsLiteral(interaction.UninstallMessage.Trim(), "uninstallMessage")).Append("\r\n");
                }

                builder.Append("        }\r\n");
                builder.Append("    }\r\n");
            }

            builder.Append("}\r\n");
            return builder.ToString();
        }

        /// <summary>Toolkit language code to force, or null when the toolkit detects the language of the user.</summary>
        public static string UiLanguage(PsadtSection psadt, string snapshotLanguage)
        {
            if (string.IsNullOrWhiteSpace(psadt.UiLanguage))
            {
                return string.Equals(snapshotLanguage, "de", StringComparison.OrdinalIgnoreCase) ? "de" : "en";
            }

            return psadt.UiLanguage == ConfigurationValidator.AutomaticLanguage ? null : psadt.UiLanguage;
        }

        /// <summary>
        /// A string for <c>config.psd1</c>. The toolkit runs every config string through <c>ExpandString</c>, so the characters that
        /// start an expansion or end the string are escaped with a backtick; control characters are refused.
        /// </summary>
        internal static string ExpandableLiteral(string value, string name)
        {
            var text = PowerShellLiteral.SingleQuoted(value, name);
            var inner = text.Substring(1, text.Length - 2);
            var escaped = new StringBuilder(inner.Length + 8);
            foreach (var c in inner)
            {
                if (c == '`' || c == '$' || c == '"')
                {
                    escaped.Append('`');
                }

                escaped.Append(c);
            }

            return "'" + escaped + "'";
        }

        /// <summary>A string for <c>strings.psd1</c>. The toolkit does not expand these; only braces would be read as lookups (the validator refuses them).</summary>
        internal static string StringsLiteral(string value, string name)
        {
            return PowerShellLiteral.SingleQuoted(value, name);
        }

        /// <summary><c>#RRGGBB</c> as the unquoted hex literal the toolkit expects (<c>0xFFRRGGBB</c>, opaque).</summary>
        internal static string AccentLiteral(string color)
        {
            if (color == null || color.Length != 7 || color[0] != '#' || color.Skip(1).Any(c => !Uri.IsHexDigit(c)))
            {
                throw new ArgumentException("The accent colour must be #RRGGBB.", "color");
            }

            return "0xFF" + color.Substring(1).ToUpperInvariant();
        }

        public static void WriteTo(string path, string content)
        {
            AtomicFile.WriteAllText(path, content, new UTF8Encoding(true));
        }

        private static void AppendAsset(StringBuilder builder, string key, string file)
        {
            if (!string.IsNullOrWhiteSpace(file))
            {
                builder.Append("        ").Append(key).Append(" = ").Append(ExpandableLiteral("..\\" + AssetsFolder + "\\" + file.Trim(), key)).Append("\r\n");
            }
        }

        private static void AppendPair(StringBuilder builder, string indent, string key, string text, string name)
        {
            builder.Append(indent).Append(key).Append(" = ").Append(StringsLiteral(text, name)).Append("\r\n");
        }
    }
}
