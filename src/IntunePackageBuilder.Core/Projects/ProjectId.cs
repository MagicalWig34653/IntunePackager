using System;
using System.Globalization;
using System.Text;

namespace IntunePackageBuilder.Core.Projects
{
    public enum ProjectIdProblem
    {
        None,
        Empty,
        TooLong,
        InvalidCharacters,
        InvalidEdge,
        ReservedName
    }

    /// <summary>
    /// Project IDs double as folder names. They are lower-case ASCII letters, digits and hyphens,
    /// must start and end with a letter or digit, and must not be a reserved Windows device name.
    /// </summary>
    public static class ProjectId
    {
        public const int MaxLength = 64;
        public const string Fallback = "project";

        private static readonly string[] ReservedNames = BuildReservedNames();

        public static ProjectIdProblem Check(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return ProjectIdProblem.Empty;
            }

            if (id.Length > MaxLength)
            {
                return ProjectIdProblem.TooLong;
            }

            foreach (var c in id)
            {
                if (!IsAllowed(c))
                {
                    return ProjectIdProblem.InvalidCharacters;
                }
            }

            if (id[0] == '-' || id[id.Length - 1] == '-')
            {
                return ProjectIdProblem.InvalidEdge;
            }

            if (Array.IndexOf(ReservedNames, id) >= 0)
            {
                return ProjectIdProblem.ReservedName;
            }

            return ProjectIdProblem.None;
        }

        public static bool IsValid(string id)
        {
            return Check(id) == ProjectIdProblem.None;
        }

        /// <summary>Suggests a valid project ID for a display name. Always returns a valid ID.</summary>
        public static string Suggest(string displayName)
        {
            var folded = FoldGermanLetters((displayName ?? string.Empty).Trim().ToLowerInvariant());
            var decomposed = folded.Normalize(NormalizationForm.FormD);

            var builder = new StringBuilder();
            var pendingHyphen = false;
            foreach (var c in decomposed)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                {
                    continue;
                }

                if (c >= 'a' && c <= 'z' || c >= '0' && c <= '9')
                {
                    if (pendingHyphen && builder.Length > 0)
                    {
                        builder.Append('-');
                    }

                    pendingHyphen = false;
                    builder.Append(c);
                }
                else
                {
                    pendingHyphen = true;
                }
            }

            var result = builder.ToString();
            if (result.Length > MaxLength)
            {
                result = result.Substring(0, MaxLength).TrimEnd('-');
            }

            if (result.Length == 0)
            {
                return Fallback;
            }

            if (Array.IndexOf(ReservedNames, result) >= 0)
            {
                return result + "-" + Fallback;
            }

            return result;
        }

        private static bool IsAllowed(char c)
        {
            return c >= 'a' && c <= 'z' || c >= '0' && c <= '9' || c == '-';
        }

        // German umlauts and eszett become two-letter forms before accents are stripped.
        private static string FoldGermanLetters(string text)
        {
            return text
                .Replace("\u00e4", "ae")
                .Replace("\u00f6", "oe")
                .Replace("\u00fc", "ue")
                .Replace("\u00df", "ss");
        }

        private static string[] BuildReservedNames()
        {
            var names = new System.Collections.Generic.List<string> { "con", "prn", "aux", "nul" };
            for (var i = 1; i <= 9; i++)
            {
                names.Add("com" + i.ToString(CultureInfo.InvariantCulture));
                names.Add("lpt" + i.ToString(CultureInfo.InvariantCulture));
            }

            return names.ToArray();
        }
    }
}
