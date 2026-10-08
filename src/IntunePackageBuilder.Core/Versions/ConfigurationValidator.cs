using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace IntunePackageBuilder.Core.Versions
{
    public enum ValidationCode
    {
        Required,
        InvalidVersion,
        InvalidProductCode,
        UnsafePath,
        InvalidPath,
        InvalidTimeout,
        ConflictingExitCodes,
        ForcedRestartCodeNotAllowed,
        InvalidShortcut,
        WrongDetectionMethod
    }

    /// <summary>A problem found in a configuration. The UI maps <see cref="Code"/> to a localized text.</summary>
    public sealed class ValidationIssue
    {
        public ValidationIssue(string field, ValidationCode code)
        {
            Field = field;
            Code = code;
        }

        /// <summary>JSON path of the offending value, for example <c>install.arguments</c>.</summary>
        public string Field { get; private set; }

        public ValidationCode Code { get; private set; }

        public override string ToString()
        {
            return Field + ": " + Code;
        }
    }

    /// <summary>
    /// Checks that a configuration is complete enough to build a package. An EXE is never completed
    /// with assumed switches: missing vendor values are reported (SPEC section 5.2, check A03).
    /// </summary>
    public static class ConfigurationValidator
    {
        public const int MaxTimeoutMinutes = 1440;
        public const int InstallerRestartedCode = 1641;

        private static readonly Regex ProductCodePattern = new Regex(
            @"^\{[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\}$",
            RegexOptions.CultureInvariant);

        private static readonly Regex DriveRootedPattern = new Regex(@"^[A-Za-z]:[\\/]", RegexOptions.CultureInvariant);

        public static IReadOnlyList<ValidationIssue> Validate(PackageVersionConfig config)
        {
            var issues = new List<ValidationIssue>();
            if (config == null)
            {
                throw new ArgumentNullException("config");
            }

            RequireText(issues, "identity.softwareName", config.Identity.SoftwareName);
            RequireText(issues, "identity.manufacturer", config.Identity.Manufacturer);

            VersionNumber target;
            if (string.IsNullOrWhiteSpace(config.Identity.TargetVersion))
            {
                issues.Add(new ValidationIssue("identity.targetVersion", ValidationCode.Required));
            }
            else if (!VersionNumber.TryParse(config.Identity.TargetVersion, out target))
            {
                issues.Add(new ValidationIssue("identity.targetVersion", ValidationCode.InvalidVersion));
            }

            ValidateInstallerPath(issues, config.Source.InstallerRelativePath);

            if (config.Source.InstallerType == InstallerType.Msi)
            {
                ValidateMsi(issues, config);
            }
            else
            {
                ValidateExe(issues, config);
            }

            ValidateRuntime(issues, config.Runtime);
            ValidateShortcuts(issues, config.PostInstall);
            return issues;
        }

        public static bool IsProductCode(string value)
        {
            return value != null && ProductCodePattern.IsMatch(value);
        }

        private static void ValidateMsi(List<ValidationIssue> issues, PackageVersionConfig config)
        {
            if (string.IsNullOrWhiteSpace(config.Install.ProductCode))
            {
                issues.Add(new ValidationIssue("install.productCode", ValidationCode.Required));
            }
            else if (!IsProductCode(config.Install.ProductCode))
            {
                issues.Add(new ValidationIssue("install.productCode", ValidationCode.InvalidProductCode));
            }

            if (!string.IsNullOrWhiteSpace(config.Uninstall.ProductCode) && !IsProductCode(config.Uninstall.ProductCode))
            {
                issues.Add(new ValidationIssue("uninstall.productCode", ValidationCode.InvalidProductCode));
            }

            if (config.Detection.Method != DetectionMethod.MsiProductCode)
            {
                issues.Add(new ValidationIssue("detection.method", ValidationCode.WrongDetectionMethod));
            }

            ValidateMinimumVersion(issues, config.Detection.MinimumVersion);
        }

        private static void ValidateExe(List<ValidationIssue> issues, PackageVersionConfig config)
        {
            RequireText(issues, "install.arguments", config.Install.Arguments);
            RequireText(issues, "uninstall.executablePath", config.Uninstall.ExecutablePath);
            RequireText(issues, "uninstall.arguments", config.Uninstall.Arguments);

            if (config.Detection.Method != DetectionMethod.FileVersion)
            {
                issues.Add(new ValidationIssue("detection.method", ValidationCode.WrongDetectionMethod));
            }

            if (string.IsNullOrWhiteSpace(config.Detection.Path))
            {
                issues.Add(new ValidationIssue("detection.path", ValidationCode.Required));
            }
            else if (!IsAbsoluteWindowsPath(config.Detection.Path))
            {
                issues.Add(new ValidationIssue("detection.path", ValidationCode.InvalidPath));
            }

            if (!string.IsNullOrWhiteSpace(config.Uninstall.ExecutablePath) && !IsAbsoluteWindowsPath(config.Uninstall.ExecutablePath))
            {
                issues.Add(new ValidationIssue("uninstall.executablePath", ValidationCode.InvalidPath));
            }

            ValidateMinimumVersion(issues, config.Detection.MinimumVersion);
        }

        private static void ValidateMinimumVersion(List<ValidationIssue> issues, string minimumVersion)
        {
            VersionNumber parsed;
            if (string.IsNullOrWhiteSpace(minimumVersion))
            {
                issues.Add(new ValidationIssue("detection.minimumVersion", ValidationCode.Required));
            }
            else if (!VersionNumber.TryParse(minimumVersion, out parsed))
            {
                issues.Add(new ValidationIssue("detection.minimumVersion", ValidationCode.InvalidVersion));
            }
        }

        private static void ValidateInstallerPath(List<ValidationIssue> issues, string relativePath)
        {
            const string field = "source.installerRelativePath";
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                issues.Add(new ValidationIssue(field, ValidationCode.Required));
                return;
            }

            if (!IsSafeRelativePath(relativePath))
            {
                issues.Add(new ValidationIssue(field, ValidationCode.UnsafePath));
            }
        }

        private static void ValidateRuntime(List<ValidationIssue> issues, RuntimeSection runtime)
        {
            if (runtime.TimeoutMinutes < 1 || runtime.TimeoutMinutes > MaxTimeoutMinutes)
            {
                issues.Add(new ValidationIssue("runtime.timeoutMinutes", ValidationCode.InvalidTimeout));
            }

            if (runtime.SuccessCodes.Contains(InstallerRestartedCode))
            {
                issues.Add(new ValidationIssue("runtime.successCodes", ValidationCode.ForcedRestartCodeNotAllowed));
            }

            if (Overlaps(runtime.SuccessCodes, runtime.RebootCodes)
                || Overlaps(runtime.SuccessCodes, runtime.RetryCodes)
                || Overlaps(runtime.RebootCodes, runtime.RetryCodes))
            {
                issues.Add(new ValidationIssue("runtime", ValidationCode.ConflictingExitCodes));
            }
        }

        private static void ValidateShortcuts(List<ValidationIssue> issues, PostInstallSection postInstall)
        {
            for (var i = 0; i < postInstall.SharedShortcutsToRemove.Count; i++)
            {
                var shortcut = postInstall.SharedShortcutsToRemove[i];
                var path = shortcut == null ? null : shortcut.RelativePath;
                if (string.IsNullOrWhiteSpace(path)
                    || !IsSafeRelativePath(path)
                    || !path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
                {
                    issues.Add(new ValidationIssue("postInstall.sharedShortcutsToRemove[" + i + "]", ValidationCode.InvalidShortcut));
                }
            }
        }

        private static void RequireText(List<ValidationIssue> issues, string field, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                issues.Add(new ValidationIssue(field, ValidationCode.Required));
            }
        }

        /// <summary>A relative path without drive, UNC prefix, rooted start, empty or dot segments.</summary>
        internal static bool IsSafeRelativePath(string path)
        {
            if (path.Length == 0 || path[0] == '\\' || path[0] == '/' || path.IndexOf(':') >= 0)
            {
                return false;
            }

            foreach (var segment in path.Split('\\', '/'))
            {
                if (segment.Length == 0 || segment == "." || segment == "..")
                {
                    return false;
                }

                if (segment.IndexOfAny(new[] { '<', '>', '"', '|', '?', '*' }) >= 0)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsAbsoluteWindowsPath(string path)
        {
            var trimmed = path.Trim();
            return DriveRootedPattern.IsMatch(trimmed) || trimmed.StartsWith("%", StringComparison.Ordinal);
        }

        private static bool Overlaps(List<int> left, List<int> right)
        {
            foreach (var value in left)
            {
                if (right.Contains(value))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
