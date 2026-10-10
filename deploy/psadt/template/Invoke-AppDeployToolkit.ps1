# Entry script of a package that uses the PSAppDeployToolkit (Windows PowerShell 5.1, 64-bit, LocalSystem). Started by Install.cmd.
# The toolkit provides the session, the dialogs in the user's session and its own log. The installer is started, tracked and
# verified by DeployCore.psm1 exactly as in the native wrapper, so exit codes and the target-state check are the same for both engines.
#
# Nothing is ended by force. The toolkit's own "close programs" and silent paths stop programs with Stop-Process -Force, so this
# script only lets the toolkit ask when the user at the console can close the programs themselves; in every other case it ends
# with the retry result and Intune tries again later.
[CmdletBinding()]
param(
    [ValidateSet('Install', 'Uninstall')]
    [string]$DeploymentType = 'Install',

    [ValidateSet('Auto', 'Interactive', 'NonInteractive', 'Silent')]
    [string]$DeployMode = 'Auto',

    # For tests and diagnostics: replaces the log folder of the configuration.
    [string]$LogDirectory
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

# Exit codes of the entry script itself (the toolkit reserves 60000 to 68999 for its built-in codes).
$script:ConfigurationError = 60002
$script:Not64Bit = 60006
$script:ToolkitFailed = 60008
$script:Unexpected = 60099

function Stop-Entry {
    param([int]$ExitCode, [string]$Message)
    $Host.UI.WriteErrorLine($Message)
    exit $ExitCode
}

try {
    Import-Module -Name (Join-Path $PSScriptRoot 'DeployCore.psm1') -Force -DisableNameChecking
}
catch {
    Stop-Entry -ExitCode $script:Unexpected -Message ("The runtime could not be loaded: " + $_.Exception.Message)
}

if ([Environment]::Is64BitOperatingSystem -and -not [Environment]::Is64BitProcess) {
    Stop-Entry -ExitCode $script:Not64Bit -Message 'The entry script must run in a 64-bit process.'
}

try {
    $config = Read-DeployConfig -Path (Join-Path $PSScriptRoot 'Deployment.config.json')
    if ([string](Get-ConfigValue -Object $config -Name 'engine' -Default '') -ne 'Psadt' -or $null -eq (Get-ConfigValue -Object $config -Name 'psadt')) {
        throw (New-DeployError -ExitCode 60002 -Message 'The configuration does not select the PSAppDeployToolkit.')
    }
}
catch {
    $code = $script:ConfigurationError
    if ($_.Exception.Data -and $_.Exception.Data.Contains('ExitCode')) { $code = [int]$_.Exception.Data['ExitCode'] }
    Stop-Entry -ExitCode $code -Message $_.Exception.Message
}
$script:Options = $config.psadt

# ---------------------------------------------------------------- the toolkit session

$toolkitManifest = Join-Path $PSScriptRoot 'PSAppDeployToolkit\PSAppDeployToolkit.psd1'
if (-not (Test-Path -LiteralPath $toolkitManifest -PathType Leaf)) {
    Stop-Entry -ExitCode $script:ToolkitFailed -Message 'The PSAppDeployToolkit folder is missing from the package.'
}

$successCodes = @($config.returnCodes | Where-Object { $_.type -eq 'Success' } | ForEach-Object { [int]$_.code })
$rebootCodes = @($config.returnCodes | Where-Object { $_.type -eq 'SoftReboot' } | ForEach-Object { [int]$_.code })
if ($successCodes.Count -eq 0) { $successCodes = @(0) }

$sessionParameters = @{
    DeploymentType              = $DeploymentType
    DeployMode                  = $DeployMode
    AppVendor                   = [string]$config.manufacturer
    AppName                     = [string]$config.softwareName
    AppVersion                  = [string]$config.targetVersion
    AppArch                     = $(if ([string]$config.install.targetArchitecture -eq 'X86') { 'x86' } else { 'x64' })
    AppLang                     = ([string]$config.language).ToUpperInvariant()
    AppRevision                 = '01'
    AppSuccessExitCodes         = $successCodes
    AppRebootExitCodes          = $rebootCodes
    AppScriptAuthor             = 'Intune Package Builder'
    RequireAdmin                = $true
    DeployAppScriptFriendlyName = 'Invoke-AppDeployToolkit.ps1'
    DisableDefaultMsiProcessList = $true
    PassThru                    = $true
}

try {
    Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'PSAppDeployToolkit') -Recurse -File | Unblock-File -ErrorAction Ignore
    Import-Module -Name $toolkitManifest -Force
    $script:AdtSession = Open-ADTSession @sessionParameters
}
catch {
    Stop-Entry -ExitCode $script:ToolkitFailed -Message (Out-String -InputObject $_ -Width 4096)
}

# ---------------------------------------------------------------- user interaction through the toolkit

function Test-SessionInteractive {
    return -not ($script:AdtSession.IsSilent() -or $script:AdtSession.IsNonInteractive())
}

# Decides whether the toolkit may ask the user to close programs and, if so, lets it. Returns 'Ready' or 'Retry'.
function Request-PsadtClose {
    param($Config, [string]$Action, [double]$MaxWaitMinutes)
    $options = $script:Options
    $names = @(Get-ConfigValue -Object $Config -Name 'processesToClose' -Default @() |
            ForEach-Object { [System.IO.Path]::GetFileNameWithoutExtension(([string]$_).Trim()) } |
            Where-Object { $_.Length -gt 0 } |
            Sort-Object -Unique)
    $running = @()
    if ($names.Count -gt 0) { $running = @(Get-RunningTargetProcess -Names $names) }

    $welcome = @{}
    if ($running.Count -gt 0) {
        $sessionId = Get-InteractiveSessionId
        if ($null -eq $sessionId -or -not (Test-SessionInteractive)) {
            Write-DeployLog -Level 'WARN' -Message 'Programs to close are running and nobody can be asked (no user at the console or no dialogs allowed). Ending with the retry result.'
            return 'Retry'
        }
        $foreign = @($running | Where-Object { $_.SessionId -ne $sessionId })
        if ($foreign.Count -gt 0) {
            Write-DeployLog -Level 'WARN' -Message 'Programs to close are running in a session other than the one at the console; the toolkit could only end them by force. Ending with the retry result.'
            return 'Retry'
        }
        Write-DeployLog -Level 'INFO' -Message ('Asking the user to close: ' + ((@($running | ForEach-Object { $_.ProcessName } | Sort-Object -Unique)) -join ', '))
        # No "close programs" button: the user closes the programs and is asked to save, nothing is ended by force.
        $welcome['CloseProcesses'] = $names
        $welcome['HideCloseButton'] = $true
        $welcome['PersistPrompt'] = $true
        if ($options.allowDefer) {
            $welcome['AllowDeferCloseProcesses'] = $true
            $welcome['DeferTimes'] = [int]$options.deferTimes
        }
        if ($options.blockExecution) { $welcome['BlockExecution'] = $true }
    }
    if ($options.checkDiskSpace) {
        $welcome['CheckDiskSpace'] = $true
        if ([int]$options.requiredDiskSpaceMb -gt 0) { $welcome['RequiredDiskSpace'] = [int]$options.requiredDiskSpaceMb }
    }
    if ($welcome.Count -gt 0) {
        Show-ADTInstallationWelcome @welcome
    }
    return 'Ready'
}

function Show-PsadtProgress {
    param($Config, [string]$Action)
    if (-not $script:Options.showProgress -or -not (Test-SessionInteractive)) { return }
    try { Show-ADTInstallationProgress }
    catch { Write-DeployLog -Level 'WARN' -Message ("The progress window could not be shown: " + $_.Exception.Message) }
}

# ---------------------------------------------------------------- run

$exitCode = $script:Unexpected
try {
    $interaction = @{
        RequestClose = { param($Config, $Action, $MaxWaitMinutes) Request-PsadtClose -Config $Config -Action $Action -MaxWaitMinutes $MaxWaitMinutes }
        ShowProgress = { param($Config, $Action) Show-PsadtProgress -Config $Config -Action $Action }
    }
    $result = @(Invoke-Deployment -DeploymentType $DeploymentType -PackageRoot $PSScriptRoot -LogDirectory $LogDirectory -Interaction $interaction)
    # The last value is the exit code; anything before it would be stray output.
    $exitCode = [int]$result[-1]
}
catch {
    try { Write-ADTLogEntry -Message ("Unexpected error: " + $_.Exception.Message) -Severity 3 }
    catch { Write-Verbose 'The toolkit log is not available.' }
    $exitCode = $script:Unexpected
}

Close-ADTSession -ExitCode $exitCode
exit $exitCode
