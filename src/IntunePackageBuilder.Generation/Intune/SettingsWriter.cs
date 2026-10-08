using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using IntunePackageBuilder.Core.Storage;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace IntunePackageBuilder.Generation.Intune
{
    /// <summary>
    /// Renders <see cref="IntuneSettings"/> as JSON and CSV. Both carry the same values: the CSV is built from the
    /// JSON document, one row per value. Every cell is escaped for its format, so names and other metadata
    /// cannot break the file or act as spreadsheet formulas (SPEC sections 9 and 10, check A17).
    /// </summary>
    public static class SettingsWriter
    {
        public const string CsvHeader = "Key,Value";

        public static string ToJson(IntuneSettings settings)
        {
            return JsonConvert.SerializeObject(settings, JsonFormat.CreateSettings());
        }

        /// <summary>The settings as flat <c>key, value</c> pairs, for example <c>program.installCommand</c> or <c>returnCodes[0].code</c>.</summary>
        public static IReadOnlyList<KeyValuePair<string, string>> Flatten(IntuneSettings settings)
        {
            // Parsed without date detection so that text such as "2026-10-08" stays exactly as written.
            var document = JsonFormat.ParseObject(ToJson(settings));
            var pairs = new List<KeyValuePair<string, string>>();
            FlattenInto(document, string.Empty, pairs);
            return pairs;
        }

        /// <summary>
        /// CSV with the header <c>Key,Value</c>, comma separated, CRLF line ends. Cells containing a comma, a
        /// quote or a line break are quoted. Text that a spreadsheet could read as a formula (leading <c>=</c>,
        /// <c>+</c>, <c>-</c>, <c>@</c>, tab or carriage return) gets a leading apostrophe; plain numbers are not touched.
        /// </summary>
        public static string ToCsv(IntuneSettings settings)
        {
            var builder = new StringBuilder();
            builder.Append(CsvHeader).Append("\r\n");
            foreach (var pair in Flatten(settings))
            {
                builder.Append(CsvCell(pair.Key)).Append(',').Append(CsvCell(pair.Value)).Append("\r\n");
            }

            return builder.ToString();
        }

        /// <summary>
        /// Writes <c>Einstellungen.json</c> (UTF-8) and <c>Einstellungen.csv</c> (UTF-8 with BOM, so spreadsheets
        /// read umlauts correctly) into <paramref name="directory"/>, each atomically.
        /// </summary>
        public static void WriteFiles(IntuneSettings settings, string directory)
        {
            AtomicFile.WriteAllText(System.IO.Path.Combine(directory, DeploymentInterface.SettingsJsonFile), ToJson(settings) + "\r\n");
            AtomicFile.WriteAllText(System.IO.Path.Combine(directory, DeploymentInterface.SettingsCsvFile), ToCsv(settings), new UTF8Encoding(true));
        }

        internal static string CsvCell(string value)
        {
            var text = value ?? string.Empty;
            if (text.Length > 0 && IsFormulaStart(text[0]) && !IsPlainNumber(text))
            {
                text = "'" + text;
            }

            if (text.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0)
            {
                return "\"" + text.Replace("\"", "\"\"") + "\"";
            }

            return text;
        }

        private static bool IsFormulaStart(char c)
        {
            return c == '=' || c == '+' || c == '-' || c == '@' || c == '\t' || c == '\r';
        }

        private static bool IsPlainNumber(string text)
        {
            decimal ignored;
            return decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out ignored)
                && !text.StartsWith("+", StringComparison.Ordinal);
        }

        private static void FlattenInto(JToken token, string path, List<KeyValuePair<string, string>> pairs)
        {
            switch (token.Type)
            {
                case JTokenType.Object:
                    foreach (var property in ((JObject)token).Properties())
                    {
                        FlattenInto(property.Value, path.Length == 0 ? property.Name : path + "." + property.Name, pairs);
                    }

                    break;
                case JTokenType.Array:
                    var index = 0;
                    foreach (var item in (JArray)token)
                    {
                        FlattenInto(item, path + "[" + index.ToString(CultureInfo.InvariantCulture) + "]", pairs);
                        index++;
                    }

                    break;
                case JTokenType.Null:
                    break;
                case JTokenType.Boolean:
                    pairs.Add(new KeyValuePair<string, string>(path, token.Value<bool>() ? "true" : "false"));
                    break;
                default:
                    pairs.Add(new KeyValuePair<string, string>(path, Convert.ToString(token.ToObject<object>(), CultureInfo.InvariantCulture)));
                    break;
            }
        }
    }
}
