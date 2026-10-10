using System.Linq;
using IntunePackageBuilder.Core.Versions;
using Xunit;

namespace IntunePackageBuilder.Core.Tests
{
    public class ConfigurationValidatorTests
    {
        private const string ProductCode = "{8F2C41A7-5B3E-4D19-A7C0-3E6D21B94F05}";

        private static PackageVersionConfig ValidMsi()
        {
            var config = PackageVersionConfig.CreateDefault("contoso-reader", InstallerType.Msi);
            config.Identity.SoftwareName = "Contoso Reader";
            config.Identity.Manufacturer = "Contoso Ltd.";
            config.Identity.TargetVersion = "4.2.1";
            config.Source.InstallerRelativePath = "setup.msi";
            config.Install.ProductCode = ProductCode;
            config.Detection.MinimumVersion = "4.2.1";
            return config;
        }

        private static PackageVersionConfig ValidExe()
        {
            var config = PackageVersionConfig.CreateDefault("fabrikam-editor", InstallerType.Exe);
            config.Identity.SoftwareName = "Fabrikam Editor";
            config.Identity.Manufacturer = "Fabrikam";
            config.Identity.TargetVersion = "12.0.3";
            config.Source.InstallerRelativePath = "setup.exe";
            config.Install.Arguments = "/quiet /norestart";
            config.Uninstall.ExecutablePath = "C:\\Program Files\\Fabrikam\\uninstall.exe";
            config.Uninstall.Arguments = "/quiet";
            config.Detection.Path = "C:\\Program Files\\Fabrikam\\editor.exe";
            config.Detection.MinimumVersion = "12.0.3";
            return config;
        }

        private static void AssertIssue(PackageVersionConfig config, string field, ValidationCode code)
        {
            var issues = ConfigurationValidator.Validate(config);
            Assert.Contains(issues, i => i.Field == field && i.Code == code);
        }

        [Fact]
        public void CompleteMsiConfigurationIsValid()
        {
            Assert.Empty(ConfigurationValidator.Validate(ValidMsi()));
        }

        [Fact]
        public void CompleteExeConfigurationIsValid()
        {
            Assert.Empty(ConfigurationValidator.Validate(ValidExe()));
        }

        [Fact]
        public void ExeWithoutVendorValuesIsNotBuildableAndNothingIsGuessed()
        {
            var config = PackageVersionConfig.CreateDefault("fabrikam-editor", InstallerType.Exe);
            config.Identity.SoftwareName = "Fabrikam Editor";
            config.Identity.Manufacturer = "Fabrikam";
            config.Identity.TargetVersion = "12.0.3";
            config.Source.InstallerRelativePath = "setup.exe";

            var issues = ConfigurationValidator.Validate(config);

            Assert.Contains(issues, i => i.Field == "install.arguments" && i.Code == ValidationCode.Required);
            Assert.Contains(issues, i => i.Field == "uninstall.executablePath" && i.Code == ValidationCode.Required);
            Assert.Contains(issues, i => i.Field == "uninstall.arguments" && i.Code == ValidationCode.Required);
            Assert.Contains(issues, i => i.Field == "detection.path" && i.Code == ValidationCode.Required);
            Assert.Null(config.Install.Arguments);
            Assert.Null(config.Uninstall.Arguments);
            Assert.Null(config.Detection.Path);
        }

        [Fact]
        public void MsiNeedsNoSilentOrDetectionPathInput()
        {
            var config = ValidMsi();

            Assert.Null(config.Install.Arguments);
            Assert.Null(config.Detection.Path);
            Assert.Empty(ConfigurationValidator.Validate(config));
        }

        [Fact]
        public void IdentityFieldsAreRequired()
        {
            var config = ValidMsi();
            config.Identity.SoftwareName = " ";
            config.Identity.Manufacturer = null;
            config.Identity.TargetVersion = "";

            AssertIssue(config, "identity.softwareName", ValidationCode.Required);
            AssertIssue(config, "identity.manufacturer", ValidationCode.Required);
            AssertIssue(config, "identity.targetVersion", ValidationCode.Required);
        }

        [Theory]
        [InlineData("1.x")]
        [InlineData("1.2.3.4.5")]
        [InlineData("v2")]
        public void TargetVersionMustBeNumeric(string version)
        {
            var config = ValidMsi();
            config.Identity.TargetVersion = version;

            AssertIssue(config, "identity.targetVersion", ValidationCode.InvalidVersion);
        }

        [Theory]
        [InlineData("")]
        [InlineData("8F2C41A7-5B3E-4D19-A7C0-3E6D21B94F05")]
        [InlineData("{8F2C41A7-5B3E-4D19-A7C0-3E6D21B94F0}")]
        [InlineData("{not-a-guid}")]
        public void MsiProductCodeMustBeABracedGuid(string productCode)
        {
            var config = ValidMsi();
            config.Install.ProductCode = productCode;

            var issues = ConfigurationValidator.Validate(config);

            Assert.Contains(issues, i => i.Field == "install.productCode");
        }

        [Fact]
        public void DetectionMethodMustMatchInstallerType()
        {
            var msi = ValidMsi();
            msi.Detection.Method = DetectionMethod.FileVersion;
            var exe = ValidExe();
            exe.Detection.Method = DetectionMethod.MsiProductCode;

            AssertIssue(msi, "detection.method", ValidationCode.WrongDetectionMethod);
            AssertIssue(exe, "detection.method", ValidationCode.WrongDetectionMethod);
        }

        [Theory]
        [InlineData("..\\setup.msi")]
        [InlineData("sub\\..\\..\\setup.msi")]
        [InlineData("a/../b.msi")]
        [InlineData("C:\\setup.msi")]
        [InlineData("\\\\server\\share\\setup.msi")]
        [InlineData("/etc/setup.msi")]
        [InlineData("sub\\\\setup.msi")]
        [InlineData("setup?.msi")]
        public void InstallerPathMustStayInsideTheSourceFolder(string path)
        {
            var config = ValidMsi();
            config.Source.InstallerRelativePath = path;

            AssertIssue(config, "source.installerRelativePath", ValidationCode.UnsafePath);
        }

        [Theory]
        [InlineData("setup.msi")]
        [InlineData("vendor\\bin\\setup.msi")]
        [InlineData("vendor/bin/setup.msi")]
        public void InstallerPathInsideTheSourceFolderIsAccepted(string path)
        {
            var config = ValidMsi();
            config.Source.InstallerRelativePath = path;

            Assert.Empty(ConfigurationValidator.Validate(config));
        }

        [Theory]
        [InlineData("relative\\editor.exe")]
        [InlineData("editor.exe")]
        public void ExeDetectionPathMustBeAbsolute(string path)
        {
            var config = ValidExe();
            config.Detection.Path = path;

            AssertIssue(config, "detection.path", ValidationCode.InvalidPath);
        }

        [Fact]
        public void ExeDetectionPathMayStartWithAnEnvironmentVariable()
        {
            var config = ValidExe();
            config.Detection.Path = "%ProgramFiles%\\Fabrikam\\editor.exe";

            Assert.Empty(ConfigurationValidator.Validate(config));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-5)]
        [InlineData(1441)]
        public void TimeoutMustBeBetweenOneMinuteAndOneDay(int minutes)
        {
            var config = ValidMsi();
            config.Runtime.TimeoutMinutes = minutes;

            AssertIssue(config, "runtime.timeoutMinutes", ValidationCode.InvalidTimeout);
        }

        [Fact]
        public void RestartAlreadyTriggeredByTheInstallerIsNeverAQuietSuccess()
        {
            var config = ValidMsi();
            config.Runtime.SuccessCodes.Add(1641);

            AssertIssue(config, "runtime.successCodes", ValidationCode.ForcedRestartCodeNotAllowed);
        }

        [Fact]
        public void ExitCodeGroupsMustNotOverlap()
        {
            var config = ValidMsi();
            config.Runtime.RetryCodes.Add(3010);

            AssertIssue(config, "runtime", ValidationCode.ConflictingExitCodes);
        }

        [Fact]
        public void DefaultExitCodesFollowTheSpecification()
        {
            var runtime = new RuntimeSection();

            Assert.Equal(new[] { 0 }, runtime.SuccessCodes.ToArray());
            Assert.Equal(new[] { 3010 }, runtime.RebootCodes.ToArray());
            Assert.Equal(new[] { 1618 }, runtime.RetryCodes.ToArray());
            Assert.Equal(60, runtime.TimeoutMinutes);
        }

        [Fact]
        public void SharedShortcutsMustBeSingleLnkFilesBelowTheRoot()
        {
            var config = ValidMsi();
            config.PostInstall.SharedShortcutsToRemove.Add(new SharedShortcut { Root = ShortcutRoot.PublicDesktop, RelativePath = "Contoso Reader.lnk" });
            config.PostInstall.SharedShortcutsToRemove.Add(new SharedShortcut { Root = ShortcutRoot.CommonStartMenu, RelativePath = "Contoso\\Reader.LNK" });

            Assert.Empty(ConfigurationValidator.Validate(config));
        }

        [Theory]
        [InlineData("..\\Users\\x\\Desktop\\a.lnk")]
        [InlineData("C:\\Users\\Public\\Desktop\\a.lnk")]
        [InlineData("notes.txt")]
        [InlineData("")]
        [InlineData("folder\\")]
        public void ShortcutOutsideTheAllowedScopeIsRejected(string path)
        {
            var config = ValidMsi();
            config.PostInstall.SharedShortcutsToRemove.Add(new SharedShortcut { Root = ShortcutRoot.PublicDesktop, RelativePath = path });

            AssertIssue(config, "postInstall.sharedShortcutsToRemove[0]", ValidationCode.InvalidShortcut);
        }

        [Theory]
        [InlineData("{8F2C41A7-5B3E-4D19-A7C0-3E6D21B94F05}", true)]
        [InlineData("{8f2c41a7-5b3e-4d19-a7c0-3e6d21b94f05}", true)]
        [InlineData("8F2C41A7-5B3E-4D19-A7C0-3E6D21B94F05", false)]
        [InlineData(null, false)]
        public void IsProductCode_RecognizesBracedGuids(string value, bool expected)
        {
            Assert.Equal(expected, ConfigurationValidator.IsProductCode(value));
        }

        private static PackageVersionConfig ValidPsadt()
        {
            var config = ValidMsi();
            config.Deployment.Engine = DeploymentEngine.Psadt;
            return config;
        }

        [Fact]
        public void TheNativeWrapperIsTheDefaultEngineAndNeedsNoToolkitSettings()
        {
            var config = ValidMsi();
            Assert.Equal(DeploymentEngine.Native, config.Deployment.Engine);
            config.Deployment.Psadt.AccentColor = "not a colour";
            Assert.Empty(ConfigurationValidator.Validate(config));
        }

        [Fact]
        public void ToolkitDefaultsAreValid()
        {
            Assert.Empty(ConfigurationValidator.Validate(ValidPsadt()));
        }

        [Theory]
        [InlineData("#0078D4", true)]
        [InlineData("#abcdef", true)]
        [InlineData("0078D4", false)]
        [InlineData("#0078D", false)]
        [InlineData("red", false)]
        public void AccentColourMustBeSixHexDigits(string colour, bool valid)
        {
            var config = ValidPsadt();
            config.Deployment.Psadt.AccentColor = colour;
            var found = ConfigurationValidator.Validate(config).Any(i => i.Field == "deployment.psadt.accentColor");
            Assert.Equal(!valid, found);
        }

        [Theory]
        [InlineData("logo.png", true)]
        [InlineData("Company logo 2.JPG", true)]
        [InlineData("..\\logo.png", false)]
        [InlineData("sub/logo.png", false)]
        [InlineData("C:\\logo.png", false)]
        [InlineData("logo.exe", false)]
        [InlineData("a..b.png", false)]
        public void ImagesAreSimpleFileNamesOfPngOrJpg(string name, bool valid)
        {
            var config = ValidPsadt();
            config.Deployment.Psadt.LogoFile = name;
            var found = ConfigurationValidator.Validate(config).Any(i => i.Field == "deployment.psadt.logoFile" && i.Code == ValidationCode.PsadtInvalidFile);
            Assert.Equal(!valid, found);
        }

        [Fact]
        public void LanguageIsAutoOrAKnownToolkitCode()
        {
            var config = ValidPsadt();
            foreach (var good in new[] { null, "auto", "de", "pt-BR", "zh-CN" })
            {
                config.Deployment.Psadt.UiLanguage = good;
                Assert.Empty(ConfigurationValidator.Validate(config));
            }

            config.Deployment.Psadt.UiLanguage = "xx";
            AssertIssue(config, "deployment.psadt.uiLanguage", ValidationCode.PsadtInvalidLanguage);
        }

        [Fact]
        public void PostponingNeedsAtLeastOneAllowedPostponement()
        {
            var config = ValidPsadt();
            config.Deployment.Psadt.AllowDefer = true;
            config.Deployment.Psadt.DeferTimes = 0;
            AssertIssue(config, "deployment.psadt.deferTimes", ValidationCode.PsadtInvalidNumber);
            config.Deployment.Psadt.DeferTimes = PsadtSection.MaxDeferTimes + 1;
            AssertIssue(config, "deployment.psadt.deferTimes", ValidationCode.PsadtInvalidNumber);
            config.Deployment.Psadt.DeferTimes = 3;
            Assert.Empty(ConfigurationValidator.Validate(config));
        }

        [Fact]
        public void DiskSpaceMustBeNonNegativeAndBounded()
        {
            var config = ValidPsadt();
            config.Deployment.Psadt.RequiredDiskSpaceMb = -1;
            AssertIssue(config, "deployment.psadt.requiredDiskSpaceMb", ValidationCode.PsadtInvalidNumber);
            config.Deployment.Psadt.RequiredDiskSpaceMb = PsadtSection.MaxRequiredDiskSpaceMb + 1;
            AssertIssue(config, "deployment.psadt.requiredDiskSpaceMb", ValidationCode.PsadtInvalidNumber);
        }

        [Fact]
        public void TheToolkitNeedsARetryCodeBecauseAPostponedInstallationEndsWithIt()
        {
            var config = ValidPsadt();
            config.Runtime.RetryCodes.Clear();
            AssertIssue(config, "runtime.retryCodes", ValidationCode.PsadtRetryCodeMissing);
        }

        [Fact]
        public void TextsOfTheToolkitCannotContainBracesBecauseTheToolkitReadsThemAsLookups()
        {
            var config = ValidPsadt();
            config.Interaction.DetailMessage = "Press {Ctrl}";
            AssertIssue(config, "interaction.detailMessage", ValidationCode.PsadtTextBraces);

            config.Interaction.DetailMessage = "Press Ctrl";
            config.Deployment.Psadt.CompanyName = "Contoso {x}";
            AssertIssue(config, "deployment.psadt.companyName", ValidationCode.PsadtTextBraces);

            var native = ValidMsi();
            native.Interaction.DetailMessage = "Press {Ctrl}";
            Assert.Empty(ConfigurationValidator.Validate(native));
        }
    }
}
