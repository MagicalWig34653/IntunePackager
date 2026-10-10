# Entry script of a package that uses the PSAppDeployToolkit (Windows PowerShell 5.1, 64-bit, LocalSystem). Started by Install.cmd.
# The toolkit provides the session, the dialogs in the user's session and its own log. The installer is started, tracked and
# verified by DeployCore.psm1 exactly as in the native wrapper, so exit codes and the target-state check are the same for both engines.
#
# Nothing is ended by force. Show-ADTInstallationWelcome with -CloseProcesses stops programs with Stop-Process -Force in several paths
# (the close button, a sweep after its dialog loop, its silent mode), so it is never given programs to close. This script asks the user
# with Show-ADTInstallationPrompt (branded dialogs, no kill path), waits until the user has closed the programs and otherwise ends with
# the retry result, so Intune tries again later. Show-ADTInstallationWelcome is used only for the free disk space check.
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
    try {
        $folder = Join-Path ([Environment]::GetFolderPath('Windows')) 'Logs\Intune\PackageDeploy'
        New-Item -ItemType Directory -Path $folder -Force | Out-Null
        Add-Content -LiteralPath (Join-Path $folder 'Wrapper-Startup.log') -Encoding UTF8 -Value ('{0:u} [ERROR] {1} (exit code {2})' -f [datetime]::UtcNow, $Message.Trim(), $ExitCode)
    }
    catch { Write-Verbose 'No startup log available.' }
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

# Registry counter of the postponements the user used for this deployment (the toolkit's own counter belongs to Show-ADTInstallationWelcome).
$script:DeferKey = 'HKLM:\SOFTWARE\IntunePackageBuilder\Deferrals'
$script:DeferName = '{0}|{1}|{2}' -f [string]$config.projectId, [string]$config.targetVersion, $DeploymentType

function Get-DeferCount {
    try { return [int](Get-ItemProperty -LiteralPath $script:DeferKey -Name $script:DeferName -ErrorAction Stop).($script:DeferName) }
    catch { return 0 }
}

function Set-DeferCount {
    param([int]$Count)
    if (-not (Test-Path -LiteralPath $script:DeferKey)) { New-Item -Path $script:DeferKey -Force | Out-Null }
    New-ItemProperty -LiteralPath $script:DeferKey -Name $script:DeferName -Value $Count -PropertyType DWord -Force | Out-Null
}

function Clear-DeferCount {
    Remove-ItemProperty -LiteralPath $script:DeferKey -Name $script:DeferName -ErrorAction SilentlyContinue
}

# Decides whether the user can be asked to close programs and, if so, asks with the toolkit's dialogs. Returns 'Ready' or 'Retry'.
# Nothing is ended here: the user closes the programs, or the run ends with the retry result.
function Request-PsadtClose {
    param($Config, [string]$Action, [double]$MaxWaitMinutes)
    $options = $script:Options
    $names = @(Get-ConfigValue -Object $Config -Name 'processesToClose' -Default @() |
            ForEach-Object { [System.IO.Path]::GetFileNameWithoutExtension(([string]$_).Trim()) } |
            Where-Object { $_.Length -gt 0 } |
            Sort-Object -Unique)
    $running = @()
    if ($names.Count -gt 0) { $running = @(Get-RunningTargetProcess -Names $names) }

    if ($running.Count -gt 0) {
        $sessionId = Get-InteractiveSessionId
        if ($null -eq $sessionId -or -not (Test-SessionInteractive)) {
            Write-DeployLog -Level 'WARN' -Message 'Programs to close are running and nobody can be asked (no user at the console or no dialogs allowed). Ending with the retry result.'
            return 'Retry'
        }
        $foreign = @($running | Where-Object { $_.SessionId -ne $sessionId })
        if ($foreign.Count -gt 0) {
            Write-DeployLog -Level 'WARN' -Message 'Programs to close are running in a session other than the one at the console and cannot be asked. Ending with the retry result.'
            return 'Retry'
        }

        $strings = Get-ADTStringTable
        $dialog = $strings.CloseAppsPrompt.Fluent
        $message = [string]$dialog.DialogMessage[$Action]
        $detail = [string]$strings.CloseAppsPrompt.CustomMessage
        $continueText = [string]$dialog.ButtonLeftNoProcessesText[$Action]
        $deferText = [string]$dialog.ButtonRightText
        $deadline = [datetime]::UtcNow.AddMinutes($MaxWaitMinutes)
        while ($true) {
            $running = @(Get-RunningTargetProcess -Names $names)
            if ($running.Count -eq 0) { break }
            if ([datetime]::UtcNow -gt $deadline) {
                Write-DeployLog -Level 'WARN' -Message 'The programs were not closed in time. Ending with the retry result.'
                return 'Retry'
            }
            $list = (@($running | ForEach-Object { $_.ProcessName } | Sort-Object -Unique) -join ', ')
            Write-DeployLog -Level 'INFO' -Message "Asking the user to close: $list"
            $text = $message + [Environment]::NewLine + [Environment]::NewLine + $list
            if ($detail.Trim().Length -gt 0) { $text = $text + [Environment]::NewLine + [Environment]::NewLine + $detail.Trim() }
            $prompt = @{ Message = $text; ButtonLeftText = $continueText; PersistPrompt = $true; NoExitOnTimeout = $true; Timeout = 60 }
            $canDefer = $options.allowDefer -and (Get-DeferCount) -lt [int]$options.deferTimes
            if ($canDefer) { $prompt['ButtonRightText'] = $deferText }
            $answer = Show-ADTInstallationPrompt @prompt
            if ($canDefer -and $answer -eq $deferText) {
                $used = (Get-DeferCount) + 1
                Set-DeferCount -Count $used
                Write-DeployLog -Level 'INFO' -Message "The user postponed ($used of $([int]$options.deferTimes)). Ending with the retry result."
                return 'Retry'
            }
            [void](Wait-ProcessesClosed -Names $names -Seconds 5 -PollSeconds 1)
        }
    }

    if ($options.checkDiskSpace) {
        $welcome = @{ CheckDiskSpace = $true }
        if ([int]$options.requiredDiskSpaceMb -gt 0) { $welcome['RequiredDiskSpace'] = [int]$options.requiredDiskSpaceMb }
        # No programs are given to the toolkit here, so none of its paths that stop programs by force can run.
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
        RequestClose = { param($Config, $Action, $MaxWaitMinutes) [string](@(Request-PsadtClose -Config $Config -Action $Action -MaxWaitMinutes $MaxWaitMinutes)[-1]) }
        ShowProgress = { param($Config, $Action) [void](Show-PsadtProgress -Config $Config -Action $Action) }
    }
    $result = @(Invoke-Deployment -DeploymentType $DeploymentType -PackageRoot $PSScriptRoot -LogDirectory $LogDirectory -Interaction $interaction)
    # The last value is the exit code; anything before it would be stray output.
    $exitCode = [int]$result[-1]
    if ($exitCode -eq 0 -or $rebootCodes -contains $exitCode) { Clear-DeferCount }
}
catch {
    try { Write-ADTLogEntry -Message ("Unexpected error: " + $_.Exception.Message) -Severity 3 }
    catch { Write-Verbose 'The toolkit log is not available.' }
    $exitCode = $script:Unexpected
}

try { Close-ADTSession -ExitCode $exitCode }
catch { Write-Verbose ('Close-ADTSession failed: ' + $_.Exception.Message) }
exit $exitCode
