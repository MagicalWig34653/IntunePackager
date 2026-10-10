using System;
using System.Collections.Generic;
using System.Linq;
using IntunePackageBuilder.Core.Versions;

namespace IntunePackageBuilder.Build.Workflow
{
    public enum UpdateProblem
    {
        /// <summary>The new installer is of another type (MSI/EXE) than the version it should update (SPEC 6.5).</summary>
        InstallerTypeChanged
    }

    /// <summary>An update cannot adopt the settings of its base version. Carries a code, not display text.</summary>
    public sealed class UpdateDraftException : Exception
    {
        public UpdateDraftException(UpdateProblem problem)
            : base(problem.ToString())
        {
            Problem = problem;
        }

        public UpdateProblem Problem { get; private set; }
    }

    /// <summary>
    /// Builds the draft of an update (SPEC 6.5): the settings of the base version are adopted, the values that belong to
    /// the new installer (version, product code, source) come from its analysis. Nothing of the base version is changed.
    /// </summary>
    public static class UpdateDraft
    {
        /// <exception cref="UpdateDraftException">The installer type changed; an update needs a new version without template then.</exception>
        public static PackageVersionConfig Create(SourceAnalysis fresh, PackageVersionConfig basis)
        {
            if (fresh == null)
            {
                throw new ArgumentNullException("fresh");
            }

            if (basis == null)
            {
                throw new ArgumentNullException("basis");
            }

            if (fresh.InstallerType != basis.Source.InstallerType)
            {
                throw new UpdateDraftException(UpdateProblem.InstallerTypeChanged);
            }

            var draft = fresh.CreateConfiguration();
            draft.Identity.SoftwareName = basis.Identity.SoftwareName;
            draft.Identity.Manufacturer = basis.Identity.Manufacturer;
            draft.Install.Arguments = basis.Install.Arguments;
            draft.Install.TargetArchitecture = basis.Install.TargetArchitecture;
            if (fresh.InstallerType == InstallerType.Exe)
            {
                draft.Uninstall.ExecutablePath = basis.Uninstall.ExecutablePath;
                draft.Uninstall.Arguments = basis.Uninstall.Arguments;
                draft.Detection.Path = basis.Detection.Path;
            }

            var copy = Clone(basis);
            draft.Interaction = copy.Interaction;
            draft.PostInstall = copy.PostInstall;
            draft.Runtime = copy.Runtime;
            draft.Deployment = copy.Deployment;
            return draft;
        }

        /// <summary>True when the configuration carries settings that standard mode does not show (SPEC 5.3).</summary>
        public static bool HasAdvancedSettings(PackageVersionConfig config)
        {
            if (config == null)
            {
                throw new ArgumentNullException("config");
            }

            var defaults = new RuntimeSection();
            return config.Deployment.Engine != DeploymentEngine.Native
                || config.Interaction.ProcessesToClose.Count > 0
                || !string.IsNullOrWhiteSpace(config.Interaction.InstallMessage)
                || !string.IsNullOrWhiteSpace(config.Interaction.UninstallMessage)
                || !string.IsNullOrWhiteSpace(config.Interaction.DetailMessage)
                || config.PostInstall.SharedShortcutsToRemove.Count > 0
                || (config.Source.InstallerType == InstallerType.Msi && !string.IsNullOrWhiteSpace(config.Install.Arguments))
                || config.Runtime.TimeoutMinutes != defaults.TimeoutMinutes
                || !config.Runtime.SuccessCodes.SequenceEqual(defaults.SuccessCodes)
                || !config.Runtime.RebootCodes.SequenceEqual(defaults.RebootCodes)
                || !config.Runtime.RetryCodes.SequenceEqual(defaults.RetryCodes);
        }

        /// <summary>A deep copy, so a form can change its draft without touching the stored configuration.</summary>
        public static PackageVersionConfig Clone(PackageVersionConfig source)
        {
            if (source == null)
            {
                throw new ArgumentNullException("source");
            }

            var copy = new PackageVersionConfig
            {
                SchemaVersion = source.SchemaVersion,
                ProjectId = source.ProjectId
            };
            copy.Identity.SoftwareName = source.Identity.SoftwareName;
            copy.Identity.Manufacturer = source.Identity.Manufacturer;
            copy.Identity.TargetVersion = source.Identity.TargetVersion;
            copy.Source.InstallerType = source.Source.InstallerType;
            copy.Source.InstallerRelativePath = source.Source.InstallerRelativePath;
            copy.Source.ImportKind = source.Source.ImportKind;
            copy.Install.Arguments = source.Install.Arguments;
            copy.Install.ProductCode = source.Install.ProductCode;
            copy.Install.TargetArchitecture = source.Install.TargetArchitecture;
            copy.Uninstall.ProductCode = source.Uninstall.ProductCode;
            copy.Uninstall.ExecutablePath = source.Uninstall.ExecutablePath;
            copy.Uninstall.Arguments = source.Uninstall.Arguments;
            copy.Detection.Method = source.Detection.Method;
            copy.Detection.Path = source.Detection.Path;
            copy.Detection.MinimumVersion = source.Detection.MinimumVersion;
            copy.Interaction.ProcessesToClose = new List<string>(source.Interaction.ProcessesToClose);
            copy.Interaction.InstallMessage = source.Interaction.InstallMessage;
            copy.Interaction.UninstallMessage = source.Interaction.UninstallMessage;
            copy.Interaction.DetailMessage = source.Interaction.DetailMessage;
            copy.PostInstall.SharedShortcutsToRemove = source.PostInstall.SharedShortcutsToRemove
                .Select(s => new SharedShortcut { Root = s.Root, RelativePath = s.RelativePath })
                .ToList();
            copy.Runtime.TimeoutMinutes = source.Runtime.TimeoutMinutes;
            copy.Runtime.SuccessCodes = new List<int>(source.Runtime.SuccessCodes);
            copy.Runtime.RebootCodes = new List<int>(source.Runtime.RebootCodes);
            copy.Runtime.RetryCodes = new List<int>(source.Runtime.RetryCodes);
            copy.Deployment.Engine = source.Deployment.Engine;
            var psadt = source.Deployment.Psadt;
            copy.Deployment.Psadt = new PsadtSection
            {
                DialogStyle = psadt.DialogStyle,
                AccentColor = psadt.AccentColor,
                CompanyName = psadt.CompanyName,
                UiLanguage = psadt.UiLanguage,
                LogoFile = psadt.LogoFile,
                LogoDarkFile = psadt.LogoDarkFile,
                BannerFile = psadt.BannerFile,
                BalloonNotifications = psadt.BalloonNotifications,
                ShowProgress = psadt.ShowProgress,
                AllowDefer = psadt.AllowDefer,
                DeferTimes = psadt.DeferTimes,
                CheckDiskSpace = psadt.CheckDiskSpace,
                RequiredDiskSpaceMb = psadt.RequiredDiskSpaceMb
            };
            return copy;
        }
    }
}
