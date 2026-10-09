# Entry script of the package (Windows PowerShell 5.1, 64-bit, LocalSystem). Started by Install.cmd.
# Reads Deployment.config.json from the package root and installs or uninstalls the software.
# The exit code is the exit code of the installer when it is one of the configured return codes, so Intune
# classifies it with the table of the setup guide. Codes of the wrapper itself are 60000 and above.
[CmdletBinding()]
param(
    [ValidateSet('Install', 'Uninstall')]
    [string]$DeploymentType = 'Install',

    # For tests and diagnostics: replaces the log folder of the configuration.
    [string]$LogDirectory
)

$ErrorActionPreference = 'Stop'
Import-Module -Name (Join-Path $PSScriptRoot 'DeployCore.psm1') -Force -DisableNameChecking
$result = @(Invoke-Deployment -DeploymentType $DeploymentType -PackageRoot $PSScriptRoot -LogDirectory $LogDirectory)
# The last value is the exit code; anything before it would be stray output.
exit ([int]$result[-1])
