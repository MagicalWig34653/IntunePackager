using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using IntunePackageBuilder.Core.Storage;
using IntunePackageBuilder.Core.Versions;
using IntunePackageBuilder.Generation.Intune;
using Xunit;

namespace IntunePackageBuilder.Generation.Tests
{
    /// <summary>Check A17: special characters in names, paths and notes reach every output format without breaking it.</summary>
    public class BuildArtifactsTests : IDisposable
    {
        private const string HostileName = "=cmd|' /C calc'!A0, \"q\" <b>x</b> & 'z'\nsecond line";
        private const string HostilePublisher = "Evil, Inc.\"; <img src=x onerror=alert(1)>";
        private const string HostilePath = "C:\\Apps\\a'b,\"c\"\\x<y>.exe";

        private readonly string _root = Path.Combine(Path.GetTempPath(), "ipb-artifacts-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, true);
            }
        }

        private static PackageVersionConfig HostileExe()
        {
            var config = Samples.Exe();
            config.Identity.SoftwareName = HostileName;
            config.Identity.Manufacturer = HostilePublisher;
            config.Detection.Path = HostilePath;
            config.Interaction.ProcessesToClose.Add("a\"b,c.exe");
            return config;
        }

        private IReadOnlyList<string> Write(PackageVersionConfig config, string name, string language = "en")
        {
            var intune = Path.Combine(_root, name, "intune");
            var package = Path.Combine(_root, name, "package");
            return BuildArtifacts.Write(Samples.Snapshot(config, language), null, intune, package);
        }

        [Fact]
        public void AllFilesOfABuildAreGeneratedFromOneSnapshot()
        {
            var files = Write(Samples.Msi(), "msi");

            Assert.Equal(
                new[] { "Detect-App.ps1", "Einrichtung.html", "Einstellungen.csv", "Einstellungen.json", "Deployment.config.json" }.OrderBy(n => n),
                files.Select(Path.GetFileName).OrderBy(n => n));
            Assert.All(files, f => Assert.True(File.Exists(f), f));
            Assert.Equal(Path.Combine(_root, "msi", "package", "Deployment.config.json"), files.Single(f => f.EndsWith("Deployment.config.json", StringComparison.Ordinal)));
        }

        [Fact]
        public void TheSameSnapshotProducesTheSameBytes()
        {
            var first = Write(HostileExe(), "one", "de");
            var second = Write(HostileExe(), "two", "de");

            for (var i = 0; i < first.Count; i++)
            {
                Assert.Equal(File.ReadAllBytes(first[i]), File.ReadAllBytes(second[i]));
            }
        }

        [Fact]
        public void TheEncodingOfEveryFileMatchesItsConsumer()
        {
            var files = Write(Samples.Msi(), "enc").ToDictionary(Path.GetFileName);

            Assert.True(HasBom(files["Detect-App.ps1"]), "the script must have a BOM for Windows PowerShell 5.1");
            Assert.True(HasBom(files["Deployment.config.json"]), "the wrapper reads the configuration with Windows PowerShell 5.1");
            Assert.True(HasBom(files["Einstellungen.csv"]), "spreadsheets need the BOM to read umlauts");
            Assert.False(HasBom(files["Einstellungen.json"]));
            Assert.False(HasBom(files["Einrichtung.html"]));
        }

        [Fact]
        public void JsonKeepsHostileValuesExactly()
        {
            var json = JsonFormat.ParseObject(ReadAfterWrite("json", "Einstellungen.json"));

            Assert.Equal(HostileName, (string)json["app"]["name"]);
            Assert.Equal(HostilePublisher, (string)json["app"]["publisher"]);
            Assert.Equal(HostilePath, (string)json["detection"]["rule"]["path"]);
        }

        [Fact]
        public void CsvQuotesHostileValuesAndNeutralizesFormulas()
        {
            Write(HostileExe(), "csv");
            var rows = ParseCsv(File.ReadAllText(Path.Combine(_root, "csv", "intune", "Einstellungen.csv"), Encoding.UTF8).TrimStart('\uFEFF'));
            var values = rows.Where(r => r.Count == 2).ToDictionary(r => r[0], r => r[1]);

            Assert.Equal("'" + HostileName, values["app.name"]);
            Assert.Equal(HostilePublisher, values["app.publisher"]);
            Assert.Equal(HostilePath, values["detection.rule.path"]);
            Assert.Equal("a\"b,c.exe", values["processesToClose[0]"]);
            Assert.All(rows, r => Assert.Equal(2, r.Count));
        }

        [Fact]
        public void HtmlEscapesHostileValues()
        {
            Write(HostileExe(), "html");
            var html = File.ReadAllText(Path.Combine(_root, "html", "intune", "Einrichtung.html"), Encoding.UTF8);

            Assert.DoesNotContain("<b>x</b>", html);
            Assert.DoesNotContain("<img src=x", html);
            Assert.Contains("&lt;b&gt;x&lt;/b&gt;", html);
            Assert.Contains("Evil, Inc.&quot;; &lt;img src=x onerror=alert(1)&gt;", html);
            Assert.Contains("a&#39;b,&quot;c&quot;\\x&lt;y&gt;.exe", html);
        }

        [Fact]
        public void TheDetectionScriptKeepsAHostilePathInsideItsLiteral()
        {
            Write(HostileExe(), "ps");
            var script = File.ReadAllText(Path.Combine(_root, "ps", "intune", "Detect-App.ps1"), Encoding.UTF8);

            Assert.Contains("$filePath = 'C:\\Apps\\a''b,\"c\"\\x<y>.exe'\r\n", script);
            Assert.DoesNotContain("calc", script);
            Assert.DoesNotContain("onerror", script);
        }

        [Fact]
        public void TheWrapperConfigurationKeepsHostileValuesExactly()
        {
            Write(HostileExe(), "wrap");
            var json = JsonFormat.ParseObject(File.ReadAllText(Path.Combine(_root, "wrap", "package", "Deployment.config.json"), Encoding.UTF8).TrimStart('\uFEFF'));

            Assert.Equal(HostileName, (string)json["softwareName"]);
            Assert.Equal(HostilePublisher, (string)json["manufacturer"]);
            Assert.Equal(HostilePath, (string)json["detection"]["path"]);
            Assert.Equal("a\"b,c.exe", (string)json["processesToClose"][0]);
        }

        [Fact]
        public void AnInvalidConfigurationProducesNoFiles()
        {
            var config = Samples.Exe();
            config.Install.Arguments = null;

            Assert.Throws<InvalidConfigurationException>(() => Write(config, "invalid"));
            Assert.False(Directory.Exists(Path.Combine(_root, "invalid")));
        }

        private string ReadAfterWrite(string name, string file)
        {
            Write(HostileExe(), name);
            return File.ReadAllText(Path.Combine(_root, name, "intune", file), Encoding.UTF8);
        }

        private static bool HasBom(string path)
        {
            var bytes = File.ReadAllBytes(path);
            return bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        }

        /// <summary>Minimal RFC 4180 reader: quoted cells, doubled quotes, commas and line breaks inside quotes.</summary>
        private static List<List<string>> ParseCsv(string text)
        {
            var rows = new List<List<string>>();
            var row = new List<string>();
            var cell = new StringBuilder();
            var quoted = false;
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if (quoted)
                {
                    if (c == '"' && i + 1 < text.Length && text[i + 1] == '"')
                    {
                        cell.Append('"');
                        i++;
                    }
                    else if (c == '"')
                    {
                        quoted = false;
                    }
                    else
                    {
                        cell.Append(c);
                    }
                }
                else if (c == '"')
                {
                    quoted = true;
                }
                else if (c == ',')
                {
                    row.Add(cell.ToString());
                    cell.Clear();
                }
                else if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                {
                    row.Add(cell.ToString());
                    cell.Clear();
                    rows.Add(row);
                    row = new List<string>();
                    i++;
                }
                else
                {
                    cell.Append(c);
                }
            }

            if (cell.Length > 0 || row.Count > 0)
            {
                row.Add(cell.ToString());
                rows.Add(row);
            }

            return rows.Skip(1).ToList();
        }
    }
}
