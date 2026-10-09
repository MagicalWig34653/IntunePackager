using System.Collections;
using System.Globalization;
using System.Linq;
using System.Resources;
using System.Text.RegularExpressions;
using IntunePackageBuilder.Generation.Guide;
using IntunePackageBuilder.Generation.Intune;
using Xunit;

namespace IntunePackageBuilder.Generation.Tests
{
    public class GuideGeneratorTests
    {
        private static string MsiGuide(string language = "en", IntuneSettingsOptions options = null)
        {
            return GuideGenerator.Generate(IntuneSettingsBuilder.From(Samples.Snapshot(Samples.Msi(), language), options ?? new IntuneSettingsOptions()));
        }

        private static string ExeGuide(string language = "en")
        {
            return GuideGenerator.Generate(IntuneSettingsBuilder.From(Samples.Snapshot(Samples.Exe(), language)));
        }

        [Fact]
        public void TheGuideCarriesTheValuesOfTheBuild()
        {
            var html = MsiGuide();

            Assert.Contains("<code>contoso-reader.intunewin</code>", html);
            Assert.Contains("Contoso Reader", html);
            Assert.Contains("Contoso Ltd.", html);
            Assert.Contains("4.2.1", html);
            Assert.Contains("<code>Install.cmd</code>", html);
            Assert.Contains("<code>Install.cmd -DeploymentType Uninstall</code>", html);
            Assert.Contains("<td>60</td>", html);
            Assert.Contains("<code>Detect-App.ps1</code>", html);
            Assert.Contains(Samples.ProductCode, html);
            Assert.Contains(Samples.BuildIdValue, html);
            Assert.Contains("C:\\Windows\\Logs\\Intune\\PackageDeploy\\contoso-reader", html);
            Assert.Contains("MsiInstall.log", html);
        }

        [Fact]
        public void TheGuideNamesAllIntuneAreasOfTheSpecification()
        {
            foreach (var language in new[] { "en", "de" })
            {
                var html = MsiGuide(language);

                Assert.Equal(10, Regex.Matches(html, "<h2>").Count);
                foreach (var key in new[] { "SecAppType", "SecPackage", "SecInfo", "SecProgram", "SecRequirements", "SecReturnCodes", "SecDetection", "SecAssignment", "SecDependencies", "SecTroubleshooting" })
                {
                    Assert.Contains("<h2>" + HtmlText.Escape(GuideText.Get(language, key)) + "</h2>", html);
                }
            }
        }

        [Fact]
        public void TheLanguageOfTheSnapshotDecidesTheLanguageOfTheGuide()
        {
            var german = MsiGuide("de");
            var english = MsiGuide("en");

            Assert.Contains("<html lang=\"de\">", german);
            Assert.Contains("Intune-Einrichtungsanleitung", german);
            Assert.DoesNotContain("Intune setup guide", german);
            Assert.Contains("<html lang=\"en\">", english);
            Assert.Contains("Intune setup guide", english);
            Assert.DoesNotContain("Einrichtungsanleitung", english);
        }

        [Fact]
        public void AnUnknownLanguageFallsBackToEnglish()
        {
            // A snapshot only accepts de and en; settings from another source may still carry any value.
            var settings = IntuneSettingsBuilder.From(Samples.Snapshot(Samples.Msi()));
            settings.Language = "fr";

            Assert.Contains("<html lang=\"en\">", GuideGenerator.Generate(settings));
        }

        [Fact]
        public void BothLanguagesHaveTheSameKeysAndTheSamePlaceholders()
        {
            var manager = new ResourceManager("IntunePackageBuilder.Generation.Resources.GuideStrings", typeof(GuideText).Assembly);
            var english = manager.GetResourceSet(CultureInfo.InvariantCulture, true, false).Cast<DictionaryEntry>().ToDictionary(e => (string)e.Key, e => (string)e.Value);
            var german = manager.GetResourceSet(CultureInfo.GetCultureInfo("de"), true, false).Cast<DictionaryEntry>().ToDictionary(e => (string)e.Key, e => (string)e.Value);

            Assert.NotEmpty(english);
            Assert.Equal(english.Keys.OrderBy(k => k), german.Keys.OrderBy(k => k));
            foreach (var key in english.Keys)
            {
                Assert.Equal(Placeholders(english[key]), Placeholders(german[key]));
                Assert.NotEqual(string.Empty, german[key].Trim());
            }
        }

        [Fact]
        public void TheGuideWarnsAgainstMixingBuilds()
        {
            var html = MsiGuide();

            Assert.Contains("Do not mix builds", html);
            Assert.Contains("contoso-reader.intunewin", html);
            Assert.Contains("Detect-App.ps1", html);
            Assert.Contains("(" + Samples.BuildIdValue + ")", html);
        }

        [Fact]
        public void TheGuideNeverClaimsASuccessfulUploadAssignmentOrInstallation()
        {
            foreach (var html in new[] { MsiGuide("en"), MsiGuide("de"), ExeGuide("en") })
            {
                Assert.DoesNotContain("successfully", html);
                Assert.DoesNotContain("erfolgreich", html);
            }

            Assert.Contains("does not show that anything was uploaded, assigned or installed", MsiGuide("en"));
        }

        [Fact]
        public void TheMinimumWindowsVersionIsMarkedAsPlaceholderUntilItIsConfirmed()
        {
            Assert.Contains("Placeholder", MsiGuide());
            Assert.True(IntuneSettingsBuilder.From(Samples.Snapshot(Samples.Msi())).Requirements.MinimumWindowsVersionIsPlaceholder);

            var confirmed = new IntuneSettingsOptions { MinimumWindowsVersion = "Windows 11 22H2", MinimumWindowsVersionConfirmed = true };
            var html = MsiGuide("en", confirmed);

            Assert.DoesNotContain("Placeholder", html);
            Assert.Contains("Windows 11 22H2", html);
        }

        [Fact]
        public void TheReturnCodeTableListsEveryCodeWithItsClassification()
        {
            var html = MsiGuide();

            Assert.Contains("<code>0</code></td><td>Success</td>", html);
            Assert.Contains("<code>1618</code></td><td>Retry</td>", html);
            Assert.Contains("<code>3010</code></td><td>Soft reboot</td>", html);
            Assert.Contains("<code>1641</code></td><td>Hard reboot</td>", html);
        }

        [Fact]
        public void TheDetectionSectionStatesTheScriptSettingsAndTheRealSignatureStatus()
        {
            var html = MsiGuide();

            Assert.Contains("Run script as 32-bit process on 64-bit clients</th><td>No</td>", html);
            Assert.Contains("Signature status of the script</th><td>Not signed</td>", html);
            Assert.Contains("exit code 0", html);
        }

        [Fact]
        public void AnExeGuideDescribesTheFileRuleAndHasNoMsiLogs()
        {
            var html = ExeGuide();

            Assert.Contains("The file C:\\Program Files\\Fabrikam\\editor.exe exists and has file version 12.0.3 or higher.", html);
            Assert.DoesNotContain("MsiInstall.log", html);
            Assert.DoesNotContain("MsiUninstall.log", html);
            Assert.DoesNotContain("MSI log", html);
        }

        [Fact]
        public void ProgramsToCloseAreListedAndNotTerminated()
        {
            var config = Samples.Msi();
            config.Interaction.ProcessesToClose.Add("reader.exe");
            var html = GuideGenerator.Generate(IntuneSettingsBuilder.From(Samples.Snapshot(config)));

            Assert.Contains("<li><code>reader.exe</code></li>", html);
            Assert.Contains("does not end them by force", html);
        }

        [Fact]
        public void MetadataIsEscapedSoItCannotInjectMarkup()
        {
            var config = Samples.Msi();
            config.Identity.SoftwareName = "<script>alert(1)</script> & \"x\" 'y'";
            config.Identity.Manufacturer = "</td><img src=x onerror=alert(2)>";
            config.Interaction.ProcessesToClose.Add("<b onmouseover=alert(3)>.exe");
            var html = GuideGenerator.Generate(IntuneSettingsBuilder.From(Samples.Snapshot(config)));

            Assert.DoesNotContain("<script>alert", html);
            Assert.DoesNotContain("<img src=x", html);
            Assert.DoesNotContain("<b onmouseover", html);
            Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt; &amp; &quot;x&quot; &#39;y&#39;", html);
            Assert.Contains("&lt;/td&gt;&lt;img src=x onerror=alert(2)&gt;", html);
        }

        [Fact]
        public void AnExePathIsEscapedInTheRuleAndInTheTable()
        {
            var config = Samples.Exe();
            config.Detection.Path = "C:\\x\"><img src=x onerror=alert(1)>\\a.exe";
            var html = GuideGenerator.Generate(IntuneSettingsBuilder.From(Samples.Snapshot(config)));

            Assert.DoesNotContain("<img src=x", html);
            Assert.Contains("C:\\x&quot;&gt;&lt;img src=x onerror=alert(1)&gt;\\a.exe", html);
        }

        [Fact]
        public void TheGuideHasNoScriptAndNoExternalResources()
        {
            foreach (var html in new[] { MsiGuide("en"), MsiGuide("de"), ExeGuide("en") })
            {
                Assert.DoesNotContain("<script", html);
                Assert.DoesNotContain("<link", html);
                Assert.DoesNotContain("<img", html);
                Assert.DoesNotContain("<iframe", html);
                Assert.DoesNotContain("src=", html);
                Assert.DoesNotContain("http://", html);
                Assert.DoesNotContain("https://", html);
                Assert.Contains("Content-Security-Policy", html);
                Assert.Contains("default-src &#39;none&#39;", html);
            }
        }

        [Fact]
        public void TheGuideIsPrintable()
        {
            Assert.Contains("@media print", MsiGuide());
        }

        [Fact]
        public void TheGuideRendersTheSameForTheSameSnapshot()
        {
            Assert.Equal(MsiGuide("de"), MsiGuide("de"));
        }

        [Fact]
        public void AWrittenGuideIsUtf8WithoutBomAndKeepsUmlauts()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ipb-guide-" + System.Guid.NewGuid().ToString("N") + ".html");
            try
            {
                var html = MsiGuide("de");
                GuideGenerator.WriteTo(path, html);

                var bytes = System.IO.File.ReadAllBytes(path);
                Assert.False(bytes[0] == 0xEF && bytes[1] == 0xBB);
                Assert.Equal(html, new System.Text.UTF8Encoding(false).GetString(bytes));
                Assert.Contains("Benutzerdefiniertes Erkennungsskript", html);
            }
            finally
            {
                System.IO.File.Delete(path);
            }
        }

        private static string Placeholders(string text)
        {
            return string.Join(",", Regex.Matches(text, "\\{\\d+\\}").Cast<Match>().Select(m => m.Value).OrderBy(v => v));
        }
    }
}
