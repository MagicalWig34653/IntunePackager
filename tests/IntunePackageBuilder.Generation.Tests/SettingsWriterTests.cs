using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using IntunePackageBuilder.Generation.Intune;
using Newtonsoft.Json.Linq;
using Xunit;

namespace IntunePackageBuilder.Generation.Tests
{
    public sealed class SettingsWriterTests : IDisposable
    {
        private const string Nasty = "Quote \" back\\slash 'single' <script>alert(1)</script> &amp; line1\r\nline2\ttab \u00e4\u00f6\u00fc\u00df \u20ac \uD83D\uDE00 {braces} [brackets]";

        private readonly string _root = Path.Combine(Path.GetTempPath(), "ipb-settings-writer-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, true);
            }
        }

        private static IntuneSettings SettingsFor(string name, string publisher = "Contoso Ltd.")
        {
            var config = Samples.Msi();
            config.Identity.SoftwareName = name;
            config.Identity.Manufacturer = publisher;
            return IntuneSettingsBuilder.From(Samples.Snapshot(config));
        }

        // ---- JSON ----

        [Fact]
        public void Json_UsesCamelCaseNamesAndTextEnums()
        {
            var json = JObject.Parse(SettingsWriter.ToJson(SettingsFor("Contoso Reader")));

            Assert.Equal(1, json.Value<int>("schemaVersion"));
            Assert.Equal("Win32", json["app"].Value<string>("type"));
            Assert.Equal("Install.cmd -DeploymentType Uninstall", json["program"].Value<string>("uninstallCommand"));
            Assert.Equal("DetermineBasedOnReturnCodes", json["program"].Value<string>("restartBehavior"));
            Assert.Equal("MsiProductCode", json["detection"]["rule"].Value<string>("method"));
            Assert.Equal("SoftReboot", json["returnCodes"].Single(c => c.Value<int>("code") == 3010).Value<string>("type"));
        }

        [Fact]
        public void Json_KeepsSpecialCharactersIntactAndStaysValid()
        {
            var text = SettingsWriter.ToJson(SettingsFor(Nasty, "Pub \"lisher\" \\ <b>"));

            var parsed = JObject.Parse(text);

            Assert.Equal(Nasty.Trim(), parsed["app"].Value<string>("name"));
            Assert.Equal("Pub \"lisher\" \\ <b>", parsed["app"].Value<string>("publisher"));
        }

        [Fact]
        public void Json_LeavesDateLikeTextUnchanged()
        {
            var parsed = JObject.Parse(SettingsWriter.ToJson(SettingsFor("2026-10-08T10:00:00Z")));

            Assert.Equal("2026-10-08T10:00:00Z", parsed["app"].Value<string>("name"));
        }

        [Fact]
        public void Json_DoesNotWriteNullValues()
        {
            var json = SettingsWriter.ToJson(IntuneSettingsBuilder.From(Samples.Snapshot(Samples.Exe())));

            Assert.DoesNotContain("msiInstallLog", json);
            Assert.DoesNotContain("productCode", json);
        }

        // ---- CSV ----

        [Fact]
        public void Csv_HasAHeaderCrlfLineEndsAndOneRowPerValue()
        {
            var settings = SettingsFor("Contoso Reader");

            var csv = SettingsWriter.ToCsv(settings);
            var lines = csv.Split(new[] { "\r\n" }, StringSplitOptions.None);

            Assert.Equal("Key,Value", lines[0]);
            Assert.Equal(string.Empty, lines[lines.Length - 1]);
            Assert.Equal(SettingsWriter.Flatten(settings).Count + 2, lines.Length);
            Assert.Contains("app.name,Contoso Reader", lines);
            Assert.Contains("program.installCommand,Install.cmd", lines);
            Assert.Contains("returnCodes[0].code,0", lines);
            Assert.Contains("returnCodes[3].type,SoftReboot", lines);
            Assert.Contains("detection.runAs32Bit,false", lines);
        }

        [Fact]
        public void Csv_QuotesCellsWithCommasQuotesAndLineBreaks()
        {
            var csv = SettingsWriter.ToCsv(SettingsFor("A, \"B\"\nC"));

            Assert.Contains("app.name,\"A, \"\"B\"\"\nC\"\r\n", csv);
        }

        [Theory]
        [InlineData("=HYPERLINK(\"http://evil\")", "'=HYPERLINK(")]
        [InlineData("+cmd|' /C calc'!A0", "'+cmd|")]
        [InlineData("-2+3", "'-2+3")]
        [InlineData("@SUM(1+1)", "'@SUM(")]
        public void Csv_ProtectsAgainstSpreadsheetFormulas(string name, string expectedStart)
        {
            var csv = SettingsWriter.ToCsv(SettingsFor(name));

            Assert.Contains(expectedStart, csv);
            Assert.DoesNotContain("\r\napp.name,=", csv);
            Assert.DoesNotContain("\r\napp.name,+", csv);
            Assert.DoesNotContain("\r\napp.name,-", csv);
            Assert.DoesNotContain("\r\napp.name,@", csv);
        }

        [Fact]
        public void Csv_DoesNotTouchPlainNumbersOrVersions()
        {
            var csv = SettingsWriter.ToCsv(SettingsFor("Contoso Reader"));

            Assert.Contains("program.timeoutMinutes,60\r\n", csv);
            Assert.Contains("app.version,4.2.1\r\n", csv);
            Assert.Contains("returnCodes[2].code,1641\r\n", csv);
        }

        [Fact]
        public void CsvCell_HandlesEdgeCases()
        {
            Assert.Equal(string.Empty, SettingsWriter.CsvCell(null));
            Assert.Equal("-5", SettingsWriter.CsvCell("-5"));
            Assert.Equal("'-abc", SettingsWriter.CsvCell("-abc"));
            Assert.Equal("'\tx", SettingsWriter.CsvCell("\tx"));
            Assert.Equal("plain", SettingsWriter.CsvCell("plain"));
            Assert.Equal("\"a,b\"", SettingsWriter.CsvCell("a,b"));
        }

        [Fact]
        public void Csv_AndJsonCarryTheSameValues()
        {
            var settings = SettingsFor(Nasty, "Pub, \"lisher\"");

            var rows = ParseCsv(SettingsWriter.ToCsv(settings));
            var flat = SettingsWriter.Flatten(settings);

            Assert.Equal(flat.Count, rows.Count);
            for (var i = 0; i < flat.Count; i++)
            {
                Assert.Equal(flat[i].Key, rows[i][0]);
                Assert.Equal(flat[i].Value, rows[i][1]);
            }

            var json = JObject.Parse(SettingsWriter.ToJson(settings));
            Assert.Equal(json["app"].Value<string>("name"), Value(rows, "app.name"));
            Assert.Equal(json["program"].Value<string>("installCommand"), Value(rows, "program.installCommand"));
            Assert.Equal(json["detection"]["rule"].Value<string>("productCode"), Value(rows, "detection.rule.productCode"));
            Assert.Equal(json["logs"].Value<string>("directory"), Value(rows, "logs.directory"));
        }

        [Fact]
        public void Flatten_ContainsEveryLeafOfTheJsonDocument()
        {
            var settings = IntuneSettingsBuilder.From(Samples.Snapshot(Samples.Msi()));
            var leaves = JObject.Parse(SettingsWriter.ToJson(settings)).DescendantsAndSelf().Count(t => t is JValue && t.Type != JTokenType.Null);

            Assert.Equal(leaves, SettingsWriter.Flatten(settings).Count);
        }

        // ---- Files ----

        [Fact]
        public void WriteFiles_WritesJsonWithoutBomAndCsvWithBom()
        {
            var settings = SettingsFor("Contoso \u00e4\u00f6\u00fc Reader");

            SettingsWriter.WriteFiles(settings, _root);

            var jsonBytes = File.ReadAllBytes(Path.Combine(_root, "Einstellungen.json"));
            var csvBytes = File.ReadAllBytes(Path.Combine(_root, "Einstellungen.csv"));
            Assert.NotEqual(new byte[] { 0xEF, 0xBB, 0xBF }, jsonBytes.Take(3).ToArray());
            Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, csvBytes.Take(3).ToArray());
            Assert.Contains("Contoso \u00e4\u00f6\u00fc Reader", new UTF8Encoding(false).GetString(jsonBytes));
            Assert.Contains("Contoso \u00e4\u00f6\u00fc Reader", new UTF8Encoding(true).GetString(csvBytes));
            Assert.Equal(new[] { "Einstellungen.csv", "Einstellungen.json" }, Directory.GetFiles(_root).Select(Path.GetFileName).OrderBy(n => n).ToArray());
        }

        [Fact]
        public void DeploymentInterface_NamesMatchTheSpecification()
        {
            Assert.Equal("Install.cmd", DeploymentInterface.EntryScript);
            Assert.Equal("Detect-App.ps1", DeploymentInterface.DetectionScript);
            Assert.Equal("Einrichtung.html", DeploymentInterface.GuideFile);
            Assert.Equal("beispiel-anwendung.intunewin", DeploymentInterface.PackageFileName("beispiel-anwendung"));
        }

        // ---- helpers ----

        private static string Value(List<string[]> rows, string key)
        {
            return rows.Single(r => r[0] == key)[1];
        }

        /// <summary>Minimal RFC 4180 reader for the generated CSV (skips the header, undoes the formula protection).</summary>
        private static List<string[]> ParseCsv(string csv)
        {
            var records = new List<string[]>();
            var fields = new List<string>();
            var current = new StringBuilder();
            var quoted = false;
            for (var i = 0; i < csv.Length; i++)
            {
                var c = csv[i];
                if (quoted)
                {
                    if (c == '"' && i + 1 < csv.Length && csv[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else if (c == '"')
                    {
                        quoted = false;
                    }
                    else
                    {
                        current.Append(c);
                    }
                }
                else if (c == '"')
                {
                    quoted = true;
                }
                else if (c == ',')
                {
                    fields.Add(current.ToString());
                    current.Clear();
                }
                else if (c == '\r' && i + 1 < csv.Length && csv[i + 1] == '\n')
                {
                    fields.Add(current.ToString());
                    current.Clear();
                    records.Add(fields.ToArray());
                    fields.Clear();
                    i++;
                }
                else
                {
                    current.Append(c);
                }
            }

            records.RemoveAt(0);
            foreach (var record in records)
            {
                if (record[1].StartsWith("'", StringComparison.Ordinal))
                {
                    record[1] = record[1].Substring(1);
                }
            }

            return records;
        }
    }
}
