using System.Linq;
using IntunePackageBuilder.Core.Storage;
using IntunePackageBuilder.Core.Versions;
using IntunePackageBuilder.Generation.Intune;
using IntunePackageBuilder.Generation.Psadt;
using IntunePackageBuilder.Generation.Wrapper;
using Xunit;

namespace IntunePackageBuilder.Generation.Tests
{
    public class PsadtOverlayTests
    {
        private static PackageVersionConfig Toolkit()
        {
            var config = Samples.Msi();
            config.Deployment.Engine = DeploymentEngine.Psadt;
            return config;
        }

        private static string Config(PackageVersionConfig config, string language = "en")
        {
            var snapshot = Samples.Snapshot(config, language);
            return PsadtOverlay.BuildConfig(snapshot, IntuneSettingsBuilder.From(snapshot));
        }

        private static string Strings(PackageVersionConfig config, string language)
        {
            return PsadtOverlay.BuildStrings(Samples.Snapshot(config, language), language);
        }

        [Fact]
        public void TheDefaultOverlayChangesOnlyWhatThePackageNeeds()
        {
            var text = Config(Toolkit());

            Assert.DoesNotContain("Assets", text);
            Assert.DoesNotContain("FluentAccentColor", text);
            Assert.Contains("CompanyName = 'Contoso Ltd.'", text);
            Assert.Contains("LogPath = 'C:\\Windows\\Logs\\Intune\\PackageDeploy\\contoso-reader'", text);
            Assert.Contains("DialogStyle = 'Fluent'", text);
            Assert.Contains("BalloonNotifications = $true", text);
            Assert.Contains("LanguageOverride = 'en'", text);
        }

        [Fact]
        public void ThePostponedAndTimedOutDialogsEndWithTheRetryCodeOfTheIntuneTable()
        {
            var config = Toolkit();
            config.Runtime.RetryCodes = new System.Collections.Generic.List<int> { 1619 };

            var text = Config(config);

            Assert.Contains("DefaultExitCode = 1619", text);
            Assert.Contains("DeferExitCode = 1619", text);
        }

        [Theory]
        [InlineData(60, 1200)]
        [InlineData(10, 180)]
        [InlineData(1, 60)]
        [InlineData(240, 1800)]
        public void TheDialogsGiveUpAfterAThirdOfTheTimeLimitAtMostThirtyMinutes(int timeoutMinutes, int seconds)
        {
            Assert.Equal(seconds, PsadtOverlay.DialogTimeoutSeconds(timeoutMinutes));
            var config = Toolkit();
            config.Runtime.TimeoutMinutes = timeoutMinutes;
            Assert.Contains("DefaultTimeout = " + seconds, Config(config));
        }

        [Fact]
        public void ImagesAreReferencedRelativeToTheConfigFolder()
        {
            var config = Toolkit();
            config.Deployment.Psadt.LogoFile = "logo.png";
            config.Deployment.Psadt.LogoDarkFile = "logo dark.png";
            config.Deployment.Psadt.BannerFile = "banner.jpg";

            var text = Config(config);

            Assert.Contains("Logo = '..\\Assets\\logo.png'", text);
            Assert.Contains("LogoDark = '..\\Assets\\logo dark.png'", text);
            Assert.Contains("Banner = '..\\Assets\\banner.jpg'", text);
        }

        [Theory]
        [InlineData("#0078D4", "0xFF0078D4")]
        [InlineData("#abcdef", "0xFFABCDEF")]
        public void TheAccentColourIsAnUnquotedOpaqueHexNumber(string color, string literal)
        {
            var config = Toolkit();
            config.Deployment.Psadt.AccentColor = color;
            Assert.Contains("FluentAccentColor = " + literal + "\r\n", Config(config));
        }

        [Theory]
        [InlineData("0078D4")]
        [InlineData("#12345")]
        [InlineData("#12345G")]
        [InlineData(null)]
        public void AnInvalidAccentColourIsRefusedAndNeverWrittenIntoTheFile(string color)
        {
            Assert.Throws<System.ArgumentException>(() => PsadtOverlay.AccentLiteral(color));
        }

        [Fact]
        public void ValuesThatTheToolkitExpandsCannotInjectCode()
        {
            var config = Toolkit();
            config.Deployment.Psadt.CompanyName = "Evil $(calc) `x` \"q\" 'single'";

            var text = Config(config);

            Assert.Contains("CompanyName = 'Evil `$(calc) ``x`` `\"q`\" ''single'''", text);
        }

        [Fact]
        public void ControlCharactersInAValueAreRefused()
        {
            var config = Toolkit();
            config.Deployment.Psadt.CompanyName = "Contoso\r\nInjected = 1";
            Assert.Throws<Scripts.UnsafeScriptValueException>(() => Config(config));
        }

        [Fact]
        public void TheLanguageFollowsTheBuildUnlessTheUserChoseOtherwise()
        {
            var config = Toolkit();
            Assert.Contains("LanguageOverride = 'de'", Config(config, "de"));

            config.Deployment.Psadt.UiLanguage = "fr";
            Assert.Contains("LanguageOverride = 'fr'", Config(config, "de"));

            config.Deployment.Psadt.UiLanguage = "auto";
            Assert.DoesNotContain("LanguageOverride", Config(config, "de"));
        }

        [Fact]
        public void TheCloseProgramsTextsSayTheUserClosesThemInBothLanguages()
        {
            var english = Strings(Toolkit(), "en");
            var german = Strings(Toolkit(), "de");

            Assert.Contains("Please save your work and close the following programs", english);
            Assert.Contains("Bitte speichern Sie Ihre Arbeit und schließen Sie die folgenden Programme", german);
            Assert.DoesNotContain("closed automatically", english);
            Assert.Contains("Fluent = @{", english);
            Assert.Contains("Classic = @{", german);
        }

        [Fact]
        public void ConfiguredMessagesReachTheToolkitTexts()
        {
            var config = Toolkit();
            config.Interaction.InstallMessage = "Updating Contoso's reader";
            config.Interaction.UninstallMessage = "Removing the reader";
            config.Interaction.DetailMessage = "Save your PDFs first";

            var text = Strings(config, "en");

            Assert.Contains("Install = 'Updating Contoso''s reader'", text);
            Assert.Contains("Uninstall = 'Removing the reader'", text);
            Assert.Contains("CustomMessage = 'Save your PDFs first'", text);
            Assert.Contains("ProgressPrompt = @{", text);
        }

        [Fact]
        public void WithoutConfiguredMessagesNoProgressTextIsOverridden()
        {
            var text = Strings(Toolkit(), "en");
            Assert.DoesNotContain("ProgressPrompt", text);
            Assert.DoesNotContain("CustomMessage", text);
        }

        [Fact]
        public void TheFilesAreWrittenAsUtf8WithBom()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ipb-psadt-" + System.Guid.NewGuid().ToString("N") + ".psd1");
            try
            {
                PsadtOverlay.WriteTo(path, Config(Toolkit()));
                Assert.True(System.IO.File.ReadAllBytes(path).Take(3).SequenceEqual(new byte[] { 0xEF, 0xBB, 0xBF }));
            }
            finally
            {
                System.IO.File.Delete(path);
            }
        }

        [Fact]
        public void TheWrapperConfigurationNamesTheEngineAndCarriesOnlyBehaviourSettings()
        {
            var config = Toolkit();
            config.Deployment.Psadt.AllowDefer = true;
            config.Deployment.Psadt.DeferTimes = 4;
            config.Deployment.Psadt.CheckDiskSpace = true;
            config.Deployment.Psadt.RequiredDiskSpaceMb = 2048;
            config.Deployment.Psadt.AccentColor = "#112233";
            var snapshot = Samples.Snapshot(config);

            var json = JsonFormat.ParseObject(WrapperConfigBuilder.ToJson(WrapperConfigBuilder.From(snapshot, IntuneSettingsBuilder.From(snapshot))));

            Assert.Equal("Psadt", (string)json["engine"]);
            Assert.True((bool)json["psadt"]["allowDefer"]);
            Assert.Equal(4, (int)json["psadt"]["deferTimes"]);
            Assert.Equal(2048, (int)json["psadt"]["requiredDiskSpaceMb"]);
            Assert.True((bool)json["psadt"]["showProgress"]);
            Assert.Null(json["psadt"]["accentColor"]);
        }

        [Fact]
        public void TheNativeEngineWritesNoToolkitSection()
        {
            var snapshot = Samples.Snapshot(Samples.Msi());

            var json = JsonFormat.ParseObject(WrapperConfigBuilder.ToJson(WrapperConfigBuilder.From(snapshot, IntuneSettingsBuilder.From(snapshot))));

            Assert.Equal("Native", (string)json["engine"]);
            Assert.Null(json["psadt"]);
        }
    }
}
