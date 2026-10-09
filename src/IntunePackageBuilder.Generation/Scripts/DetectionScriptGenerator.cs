using System;
using System.Text;
using IntunePackageBuilder.Core.Storage;
using IntunePackageBuilder.Core.Versions;
using IntunePackageBuilder.Generation.Intune;

namespace IntunePackageBuilder.Generation.Scripts
{
    /// <summary>
    /// Generates the standalone Intune detection script (SPEC section 8.3). The script checks the real installed
    /// state: for an MSI the uninstall registration of the ProductCode in both registry views, for an EXE the file
    /// version of the configured file. It reads no files of the package or of the Intune cache and writes no marker.
    /// Intune contract: detected means exit code 0 and non-empty standard output; otherwise nothing is written to
    /// standard output and the exit code is 1.
    /// </summary>
    public static class DetectionScriptGenerator
    {
        public const int NotDetectedExitCode = 1;

        public static string Generate(IntuneSettings settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException("settings");
            }

            var rule = settings.Detection.Rule;
            return rule.Method == DetectionMethod.MsiProductCode
                ? GenerateForMsi(settings.BuildId, rule.ProductCode, rule.MinimumVersion)
                : GenerateForFile(settings.BuildId, rule.Path, rule.MinimumVersion);
        }

        public static string GenerateForMsi(string buildId, string productCode, string minimumVersion)
        {
            if (!ConfigurationValidator.IsProductCode(productCode))
            {
                throw new UnsafeScriptValueException("productCode", "The product code is not a GUID in braces.");
            }

            var minimum = CheckedVersion(minimumVersion);
            var script = new Script(buildId);
            script.AppendCommon();
            script.Line("$productCode = " + PowerShellLiteral.SingleQuoted(productCode.Trim(), "productCode"));
            script.Line("$minimumVersion = " + PowerShellLiteral.SingleQuoted(minimum, "minimumVersion"));
            script.Line("$subKey = 'SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\' + $productCode");
            script.Blank();
            script.Line("function Get-DetectedState {");
            script.Line("    # Both registry views are read explicitly, so the result does not depend on the bitness of the process.");
            script.Line("    if ([Environment]::Is64BitOperatingSystem) {");
            script.Line("        $views = @([Microsoft.Win32.RegistryView]::Registry64, [Microsoft.Win32.RegistryView]::Registry32)");
            script.Line("    }");
            script.Line("    else {");
            script.Line("        $views = @([Microsoft.Win32.RegistryView]::Default)");
            script.Line("    }");
            script.Line("    foreach ($view in $views) {");
            script.Line("        $base = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine, $view)");
            script.Line("        try {");
            script.Line("            $key = $base.OpenSubKey($subKey)");
            script.Line("            if ($null -eq $key) { continue }");
            script.Line("            try { $displayVersion = [string]$key.GetValue('DisplayVersion') }");
            script.Line("            finally { $key.Dispose() }");
            script.Line("        }");
            script.Line("        finally { $base.Dispose() }");
            script.Line("        if (Test-VersionAtLeast -Installed $displayVersion -Minimum $minimumVersion) {");
            script.Line("            return ('Detected {0} version {1}' -f $productCode, $displayVersion.Trim())");
            script.Line("        }");
            script.Line("    }");
            script.Line("    return $null");
            script.Line("}");
            script.AppendMain();
            return script.ToString();
        }

        public static string GenerateForFile(string buildId, string path, string minimumVersion)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new UnsafeScriptValueException("path", "The detection file path is empty.");
            }

            var minimum = CheckedVersion(minimumVersion);
            var script = new Script(buildId);
            script.AppendCommon();
            script.Line("$filePath = " + PowerShellLiteral.SingleQuoted(path.Trim(), "path"));
            script.Line("$minimumVersion = " + PowerShellLiteral.SingleQuoted(minimum, "minimumVersion"));
            script.Blank();
            script.Line("function Get-DetectedState {");
            script.Line("    # Environment variables such as %ProgramFiles% resolve to the 64-bit folders in a 64-bit process.");
            script.Line("    if ([Environment]::Is64BitOperatingSystem -and -not [Environment]::Is64BitProcess) {");
            script.Line("        throw 'The detection script must run as a 64-bit process.'");
            script.Line("    }");
            script.Line("    $path = [Environment]::ExpandEnvironmentVariables($filePath)");
            script.Line("    if ($path.IndexOf('%') -ge 0 -or -not [System.IO.Path]::IsPathRooted($path)) { return $null }");
            script.Line("    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { return $null }");
            script.Line("    $info = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($path)");
            script.Line("    $fileVersion = '{0}.{1}.{2}.{3}' -f $info.FileMajorPart, $info.FileMinorPart, $info.FileBuildPart, $info.FilePrivatePart");
            script.Line("    if ($fileVersion -eq '0.0.0.0') { return $null }");
            script.Line("    if (Test-VersionAtLeast -Installed $fileVersion -Minimum $minimumVersion) {");
            script.Line("        return ('Detected {0} version {1}' -f $path, $fileVersion)");
            script.Line("    }");
            script.Line("    return $null");
            script.Line("}");
            script.AppendMain();
            return script.ToString();
        }

        /// <summary>Writes the script as UTF-8 with BOM (Windows PowerShell 5.1 reads BOM-less files as ANSI).</summary>
        public static void WriteTo(string path, string script)
        {
            AtomicFile.WriteAllText(path, script, new UTF8Encoding(true));
        }

        private static string CheckedVersion(string minimumVersion)
        {
            VersionNumber parsed;
            if (!VersionNumber.TryParse(minimumVersion, out parsed))
            {
                throw new UnsafeScriptValueException("minimumVersion", "The minimum version is not a numeric version with one to four parts.");
            }

            return parsed.Text;
        }

        private sealed class Script
        {
            private readonly StringBuilder _text = new StringBuilder();
            private readonly string _buildId;

            public Script(string buildId)
            {
                _buildId = buildId;
            }

            public void Line(string line)
            {
                _text.Append(line).Append("\r\n");
            }

            public void Blank()
            {
                _text.Append("\r\n");
            }

            public void AppendCommon()
            {
                Line("# Detection script generated by Intune Package Builder.");
                Line("# Build: " + SafeComment(_buildId));
                Line("# Checks the installed state of the software. It uses no package files and no Intune cache.");
                Line("# Detected: exit code 0 and one line on standard output. Not detected: no output and exit code 1.");
                Line("Set-StrictMode -Version 2.0");
                Line("$ErrorActionPreference = 'Stop'");
                Blank();
                Line("function ConvertTo-VersionParts {");
                Line("    param([string]$Text)");
                Line("    if ([string]::IsNullOrWhiteSpace($Text)) { return $null }");
                Line("    $match = [regex]::Match($Text.Trim(), '^v?(\\d{1,9}(?:\\.\\d{1,9}){0,3})(?![\\d.])')");
                Line("    if (-not $match.Success) { return $null }");
                Line("    $parts = @($match.Groups[1].Value.Split('.') | ForEach-Object { [int64]$_ })");
                Line("    while ($parts.Count -lt 4) { $parts += [int64]0 }");
                Line("    return $parts");
                Line("}");
                Blank();
                Line("function Test-VersionAtLeast {");
                Line("    param([string]$Installed, [string]$Minimum)");
                Line("    $installedParts = ConvertTo-VersionParts -Text $Installed");
                Line("    $minimumParts = ConvertTo-VersionParts -Text $Minimum");
                Line("    if ($null -eq $installedParts -or $null -eq $minimumParts) { return $false }");
                Line("    for ($i = 0; $i -lt 4; $i++) {");
                Line("        if ($installedParts[$i] -gt $minimumParts[$i]) { return $true }");
                Line("        if ($installedParts[$i] -lt $minimumParts[$i]) { return $false }");
                Line("    }");
                Line("    return $true");
                Line("}");
                Blank();
            }

            public void AppendMain()
            {
                Blank();
                Line("try {");
                Line("    $state = Get-DetectedState");
                Line("}");
                Line("catch {");
                Line("    [Console]::Error.WriteLine('Detection failed: ' + $_.Exception.Message)");
                Line("    exit 1");
                Line("}");
                Line("if ($state) {");
                Line("    Write-Output $state");
                Line("    exit 0");
                Line("}");
                Line("exit 1");
            }

            public override string ToString()
            {
                return _text.ToString();
            }

            private static string SafeComment(string value)
            {
                var builder = new StringBuilder();
                foreach (var c in value ?? string.Empty)
                {
                    builder.Append(c >= ' ' && c < '\u007F' ? c : '?');
                }

                return builder.ToString();
            }
        }
    }
}
