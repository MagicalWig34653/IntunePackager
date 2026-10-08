using System;
using IntunePackageBuilder.Core.Builds;
using IntunePackageBuilder.Core.Sources;
using IntunePackageBuilder.Core.Versions;

namespace IntunePackageBuilder.Generation.Tests
{
    /// <summary>Builds valid configurations and snapshots for the generator tests.</summary>
    internal static class Samples
    {
        public const string ProductCode = "{8F2C41A7-5B3E-4D19-A7C0-3E6D21B94F05}";
        public const string BuildIdValue = "20261008-153412-a3f9";
        public static readonly DateTime Created = new DateTime(2026, 10, 8, 15, 34, 12, DateTimeKind.Utc);

        public static PackageVersionConfig Msi()
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

        public static PackageVersionConfig Exe()
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

        public static SourceManifest Manifest()
        {
            var manifest = new SourceManifest { CreatedUtc = Created };
            manifest.Files.Add(new SourceFileEntry { Path = "setup.msi", Size = 3, Sha256 = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad" });
            return manifest;
        }

        public static BuildSnapshot Snapshot(PackageVersionConfig config, string language = "en")
        {
            return BuildSnapshot.Create(config, Manifest(), BuildIdValue, language, Created);
        }
    }
}
