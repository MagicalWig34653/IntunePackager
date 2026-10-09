using System;
using System.Linq;
using System.Text;
using IntunePackageBuilder.Core.Builds;
using IntunePackageBuilder.Core.Storage;
using IntunePackageBuilder.Generation.Intune;
using Newtonsoft.Json;

namespace IntunePackageBuilder.Generation.Wrapper
{
    public static class WrapperConfigBuilder
    {
        public static WrapperConfig From(BuildSnapshot snapshot, IntuneSettings settings)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException("snapshot");
            }

            if (settings == null)
            {
                throw new ArgumentNullException("settings");
            }

            if (!string.Equals(snapshot.BuildId, settings.BuildId, StringComparison.Ordinal))
            {
                throw new ArgumentException("The settings belong to another build than the snapshot.", "settings");
            }

            var config = snapshot.Configuration;
            return new WrapperConfig
            {
                BuildId = snapshot.BuildId,
                Language = snapshot.Language,
                ProjectId = config.ProjectId,
                SoftwareName = settings.App.Name,
                Manufacturer = settings.App.Publisher,
                TargetVersion = settings.App.Version,
                Install = new WrapperInstall
                {
                    InstallerType = config.Source.InstallerType,
                    InstallerPath = DeploymentInterface.PackageSourceFolder + "\\" + config.Source.InstallerRelativePath.Trim().Replace('/', '\\'),
                    Arguments = Trimmed(config.Install.Arguments),
                    ProductCode = Trimmed(config.Install.ProductCode),
                    TargetArchitecture = config.Install.TargetArchitecture
                },
                Uninstall = new WrapperUninstall
                {
                    ProductCode = Trimmed(config.Uninstall.ProductCode),
                    ExecutablePath = Trimmed(config.Uninstall.ExecutablePath),
                    Arguments = Trimmed(config.Uninstall.Arguments)
                },
                Detection = settings.Detection.Rule,
                TimeoutMinutes = settings.Program.TimeoutMinutes,
                ReturnCodes = settings.ReturnCodes.Select(e => new ReturnCodeEntry { Code = e.Code, Type = e.Type }).ToList(),
                ProcessesToClose = settings.ProcessesToClose.ToList(),
                InstallMessage = Trimmed(config.Interaction.InstallMessage),
                UninstallMessage = Trimmed(config.Interaction.UninstallMessage),
                DetailMessage = Trimmed(config.Interaction.DetailMessage),
                SharedShortcutsToRemove = config.PostInstall.SharedShortcutsToRemove
                    .Select(s => new WrapperShortcut { Root = s.Root, RelativePath = s.RelativePath.Trim() })
                    .ToList(),
                Logs = new WrapperLogs
                {
                    Directory = settings.Logs.Directory,
                    DeploymentLog = settings.Logs.DeploymentLog,
                    MsiInstallLog = settings.Logs.MsiInstallLog,
                    MsiUninstallLog = settings.Logs.MsiUninstallLog
                }
            };
        }

        public static string ToJson(WrapperConfig config)
        {
            return JsonConvert.SerializeObject(config, JsonFormat.CreateSettings());
        }

        /// <summary>Writes the file as UTF-8 with BOM: Windows PowerShell 5.1 reads BOM-less files as ANSI.</summary>
        public static void WriteTo(string path, WrapperConfig config)
        {
            AtomicFile.WriteAllText(path, ToJson(config) + "\r\n", new UTF8Encoding(true));
        }

        private static string Trimmed(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
    }
}
