# Core functions of the client runtime (Windows PowerShell 5.1, 64-bit, LocalSystem). No PowerShell 7 syntax.
# Deploy-Wrapper.ps1 calls Invoke-Deployment; the Pester tests call the single functions.
Set-StrictMode -Version 2.0

# Exit codes of the wrapper itself. Everything else is the exit code of the installer, passed through so that Intune
# classifies it with the return code table of the setup guide. 1618 asks Intune to retry later.
$script:ExitCodes = @{
    Retry              = 1618
    VerificationFailed = 60001
    ConfigurationError = 60002
    InstallerMissing   = 60003
    TimedOut           = 60004
    Not64Bit           = 60006
    Unexpected         = 60099
}

$script:LogFile = $null
$script:Messages = @{}

# ---------------------------------------------------------------- errors, configuration, logging

function New-DeployError {
    param([int]$ExitCode, [string]$Message)
    $exception = New-Object System.InvalidOperationException $Message
    $exception.Data['ExitCode'] = $ExitCode
    return $exception
}

function Get-ConfigValue {
    param($Object, [string]$Name, $Default = $null)
    if ($null -eq $Object) { return $Default }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property -or $null -eq $property.Value) { return $Default }
    return $property.Value
}

function Read-DeployConfig {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw (New-DeployError -ExitCode $script:ExitCodes.ConfigurationError -Message "Configuration file not found: $Path")
    }
    try {
        $config = [System.IO.File]::ReadAllText($Path, [System.Text.Encoding]::UTF8) | ConvertFrom-Json
    }
    catch {
        throw (New-DeployError -ExitCode $script:ExitCodes.ConfigurationError -Message "Configuration file is not valid JSON: $($_.Exception.Message)")
    }
    if ((Get-ConfigValue -Object $config -Name 'schemaVersion' -Default 0) -ne 1) {
        throw (New-DeployError -ExitCode $script:ExitCodes.ConfigurationError -Message 'Unsupported configuration schema version.')
    }
    foreach ($name in 'install', 'uninstall', 'detection', 'logs', 'returnCodes', 'timeoutMinutes') {
        if ($null -eq (Get-ConfigValue -Object $config -Name $name)) {
            if ($name -eq 'uninstall') { continue }
            throw (New-DeployError -ExitCode $script:ExitCodes.ConfigurationError -Message "Configuration lacks '$name'.")
        }
    }
    return $config
}

function Set-LogDirectoryAcl {
    param([string]$Directory)
    try {
        $acl = Get-Acl -LiteralPath $Directory
        $acl.SetAccessRuleProtection($true, $false)
        foreach ($rule in @($acl.Access)) { [void]$acl.RemoveAccessRule($rule) }
        $inherit = [System.Security.AccessControl.InheritanceFlags]'ContainerInherit, ObjectInherit'
        $none = [System.Security.AccessControl.PropagationFlags]::None
        $allow = [System.Security.AccessControl.AccessControlType]::Allow
        foreach ($sid in 'S-1-5-18', 'S-1-5-32-544') {
            $identity = New-Object System.Security.Principal.SecurityIdentifier $sid
            $rule = New-Object System.Security.AccessControl.FileSystemAccessRule($identity, 'FullControl', $inherit, $none, $allow)
            $acl.AddAccessRule($rule)
        }
        Set-Acl -LiteralPath $Directory -AclObject $acl
    }
    catch {
        Write-DeployLog -Level 'WARN' -Message "Could not restrict the log folder to SYSTEM and administrators: $($_.Exception.Message)"
    }
}

function Initialize-DeployLog {
    param([string]$Directory, [string]$FileName)
    if (-not (Test-Path -LiteralPath $Directory)) {
        New-Item -ItemType Directory -Path $Directory -Force | Out-Null
    }
    $script:LogFile = Join-Path $Directory $FileName
    Set-LogDirectoryAcl -Directory $Directory
}

function Write-DeployLog {
    param([string]$Level, [string]$Message)
    $stamp = (Get-Date).ToUniversalTime().ToString('yyyy-MM-dd HH:mm:ss', [System.Globalization.CultureInfo]::InvariantCulture)
    $line = '{0}Z {1,-5} {2}' -f $stamp, $Level, $Message
    if ($script:LogFile) {
        try {
            [System.IO.File]::AppendAllText($script:LogFile, $line + "`r`n", (New-Object System.Text.UTF8Encoding($true)))
        }
        catch {
            Write-Verbose "Log write failed: $($_.Exception.Message)"
        }
    }
    Write-Verbose $line
}

# ---------------------------------------------------------------- return codes and commands

function Get-ReturnCodeType {
    param($Config, [int]$ExitCode)
    foreach ($entry in @($Config.returnCodes)) {
        if ([int]$entry.code -eq $ExitCode) { return [string]$entry.type }
    }
    return $null
}

function ConvertTo-QuotedArgument {
    param([string]$Argument)
    $special = [char[]]@(' ', "`t", "`n", "`v", '"')
    if ($Argument.Length -gt 0 -and $Argument.IndexOfAny($special) -lt 0) { return $Argument }
    $backslash = [char]92
    $quote = [char]34
    $builder = New-Object System.Text.StringBuilder
    [void]$builder.Append($quote)
    $backslashes = 0
    foreach ($c in $Argument.ToCharArray()) {
        if ($c -eq $backslash) { $backslashes++; continue }
        if ($c -eq $quote) {
            [void]$builder.Append($backslash, ($backslashes * 2) + 1)
            [void]$builder.Append($quote)
        }
        else {
            [void]$builder.Append($backslash, $backslashes)
            [void]$builder.Append($c)
        }
        $backslashes = 0
    }
    [void]$builder.Append($backslash, $backslashes * 2)
    [void]$builder.Append($quote)
    return $builder.ToString()
}

function ConvertTo-ArgumentString {
    param([string[]]$Arguments)
    $quoted = @()
    foreach ($argument in $Arguments) { $quoted += (ConvertTo-QuotedArgument -Argument $argument) }
    return ($quoted -join ' ')
}

# Builds the command for the installer or uninstaller from the configuration. Nothing is guessed: EXE arguments and
# the uninstall program are exactly what the configuration contains.
function New-ProcessCommand {
    param($Config, [string]$Action, [string]$PackageRoot, [string]$LogDirectory)
    $install = $Config.install
    $isMsi = ([string]$install.installerType -eq 'Msi')
    $msiexec = Join-Path ([Environment]::GetFolderPath('System')) 'msiexec.exe'
    if ($Action -eq 'Install') {
        $installerPath = Join-Path $PackageRoot ([string]$install.installerPath)
        if (-not (Test-Path -LiteralPath $installerPath -PathType Leaf)) {
            throw (New-DeployError -ExitCode $script:ExitCodes.InstallerMissing -Message "The installer is missing: $installerPath")
        }
        if ($isMsi) {
            $log = Join-Path $LogDirectory ([string](Get-ConfigValue -Object $Config.logs -Name 'msiInstallLog' -Default 'MsiInstall.log'))
            $arguments = ConvertTo-ArgumentString -Arguments @('/i', $installerPath, '/qn', '/norestart', '/l*v', $log)
            $extra = [string](Get-ConfigValue -Object $install -Name 'arguments' -Default '')
            if ($extra.Trim().Length -gt 0) { $arguments = $arguments + ' ' + $extra.Trim() }
            return [pscustomobject]@{ FilePath = $msiexec; Arguments = $arguments; WorkingDirectory = (Split-Path -Parent $installerPath) }
        }
        return [pscustomobject]@{
            FilePath         = $installerPath
            Arguments        = [string](Get-ConfigValue -Object $install -Name 'arguments' -Default '')
            WorkingDirectory = (Split-Path -Parent $installerPath)
        }
    }

    $uninstall = Get-ConfigValue -Object $Config -Name 'uninstall'
    if ($isMsi) {
        $code = [string](Get-ConfigValue -Object $uninstall -Name 'productCode' -Default (Get-ConfigValue -Object $install -Name 'productCode' -Default ''))
        if ($code -notmatch '^\{[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\}$') {
            throw (New-DeployError -ExitCode $script:ExitCodes.ConfigurationError -Message 'The configuration has no valid product code for the uninstallation.')
        }
        $log = Join-Path $LogDirectory ([string](Get-ConfigValue -Object $Config.logs -Name 'msiUninstallLog' -Default 'MsiUninstall.log'))
        $arguments = ConvertTo-ArgumentString -Arguments @('/x', $code, '/qn', '/norestart', '/l*v', $log)
        return [pscustomobject]@{ FilePath = $msiexec; Arguments = $arguments; WorkingDirectory = $PackageRoot }
    }
    $program = [string](Get-ConfigValue -Object $uninstall -Name 'executablePath' -Default '')
    if ($program.Trim().Length -eq 0) {
        throw (New-DeployError -ExitCode $script:ExitCodes.ConfigurationError -Message 'The configuration has no uninstall program.')
    }
    $program = [Environment]::ExpandEnvironmentVariables($program.Trim())
    if (-not (Test-Path -LiteralPath $program -PathType Leaf)) {
        throw (New-DeployError -ExitCode $script:ExitCodes.InstallerMissing -Message "The uninstall program is missing: $program")
    }
    return [pscustomobject]@{
        FilePath         = $program
        Arguments        = [string](Get-ConfigValue -Object $uninstall -Name 'arguments' -Default '')
        WorkingDirectory = (Split-Path -Parent $program)
    }
}

# ---------------------------------------------------------------- running the installer

function Get-ProcessSnapshot {
    Get-CimInstance -ClassName Win32_Process -Property ProcessId, ParentProcessId, CreationDate |
        ForEach-Object { [pscustomobject]@{ Id = [int]$_.ProcessId; ParentId = [int]$_.ParentProcessId; Created = $_.CreationDate } }
}

# Ids of all processes below a root process. Orphans keep the id of their ended parent, so the chain is found after the root ended.
function Get-DescendantProcessId {
    param([int]$RootId, [datetime]$RootStarted)
    $all = @(Get-ProcessSnapshot)
    $found = New-Object 'System.Collections.Generic.HashSet[int]'
    $queue = New-Object 'System.Collections.Generic.Queue[int]'
    $queue.Enqueue($RootId)
    while ($queue.Count -gt 0) {
        $parent = $queue.Dequeue()
        foreach ($candidate in $all) {
            if ($candidate.ParentId -eq $parent -and $candidate.Id -ne $RootId -and $candidate.Created -ge $RootStarted) {
                if ($found.Add($candidate.Id)) { $queue.Enqueue($candidate.Id) }
            }
        }
    }
    return @($found)
}

function Test-ProcessRunning {
    param([int]$Id)
    return ($null -ne (Get-Process -Id $Id -ErrorAction SilentlyContinue))
}

# Starts the installer without a shell, waits for it and for every process it started (a bootstrapper that ends early
# is not the end of the installation). Nothing is killed: when the limit is reached the result says so.
function Invoke-DeployProcess {
    param([string]$FilePath, [string]$Arguments, [string]$WorkingDirectory, [double]$TimeoutMinutes)
    $start = New-Object System.Diagnostics.ProcessStartInfo
    $start.FileName = $FilePath
    $start.Arguments = $Arguments
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    if ($WorkingDirectory) { $start.WorkingDirectory = $WorkingDirectory }
    $deadline = [datetime]::UtcNow.AddMinutes($TimeoutMinutes)
    $process = [System.Diagnostics.Process]::Start($start)
    $started = $process.StartTime
    $rootId = $process.Id
    $tracked = New-Object 'System.Collections.Generic.HashSet[int]'
    try {
        while (-not $process.WaitForExit(500)) {
            foreach ($id in @(Get-DescendantProcessId -RootId $rootId -RootStarted $started)) { [void]$tracked.Add($id) }
            if ([datetime]::UtcNow -gt $deadline) {
                return [pscustomobject]@{ ExitCode = $null; TimedOut = $true }
            }
        }
        $exitCode = $process.ExitCode
        while ($true) {
            foreach ($id in @(Get-DescendantProcessId -RootId $rootId -RootStarted $started)) { [void]$tracked.Add($id) }
            $running = @($tracked | Where-Object { Test-ProcessRunning -Id $_ })
            if ($running.Count -eq 0) { break }
            if ([datetime]::UtcNow -gt $deadline) {
                return [pscustomobject]@{ ExitCode = $null; TimedOut = $true }
            }
            Start-Sleep -Milliseconds 500
        }
        return [pscustomobject]@{ ExitCode = [int]$exitCode; TimedOut = $false }
    }
    finally {
        $process.Dispose()
    }
}

# ---------------------------------------------------------------- target state (same rule as the detection script)

function ConvertTo-VersionParts {
    param([string]$Text)
    if ([string]::IsNullOrWhiteSpace($Text)) { return $null }
    $match = [regex]::Match($Text.Trim(), '^v?(\d{1,9}(?:\.\d{1,9}){0,3})(?![\d.])')
    if (-not $match.Success) { return $null }
    $parts = @($match.Groups[1].Value.Split('.') | ForEach-Object { [int64]$_ })
    while ($parts.Count -lt 4) { $parts += [int64]0 }
    return $parts
}

function Test-VersionAtLeast {
    param([string]$Installed, [string]$Minimum)
    $installedParts = ConvertTo-VersionParts -Text $Installed
    $minimumParts = ConvertTo-VersionParts -Text $Minimum
    if ($null -eq $installedParts -or $null -eq $minimumParts) { return $false }
    for ($i = 0; $i -lt 4; $i++) {
        if ($installedParts[$i] -gt $minimumParts[$i]) { return $true }
        if ($installedParts[$i] -lt $minimumParts[$i]) { return $false }
    }
    return $true
}

# True when the software is in the state the detection rule describes: the same checks as Detect-App.ps1, from the same configuration.
function Test-TargetState {
    param($Rule)
    $minimum = [string]$Rule.minimumVersion
    if ([string]$Rule.method -eq 'MsiProductCode') {
        $subKey = 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\' + [string]$Rule.productCode
        if ([Environment]::Is64BitOperatingSystem) {
            $views = @([Microsoft.Win32.RegistryView]::Registry64, [Microsoft.Win32.RegistryView]::Registry32)
        }
        else {
            $views = @([Microsoft.Win32.RegistryView]::Default)
        }
        foreach ($view in $views) {
            $base = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine, $view)
            try {
                $key = $base.OpenSubKey($subKey)
                if ($null -eq $key) { continue }
                try { $displayVersion = [string]$key.GetValue('DisplayVersion') }
                finally { $key.Dispose() }
            }
            finally { $base.Dispose() }
            if (Test-VersionAtLeast -Installed $displayVersion -Minimum $minimum) { return $true }
        }
        return $false
    }
    $path = [Environment]::ExpandEnvironmentVariables([string]$Rule.path)
    if ($path.IndexOf('%') -ge 0 -or -not [System.IO.Path]::IsPathRooted($path)) { return $false }
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { return $false }
    $info = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($path)
    $fileVersion = '{0}.{1}.{2}.{3}' -f $info.FileMajorPart, $info.FileMinorPart, $info.FileBuildPart, $info.FilePrivatePart
    if ($fileVersion -eq '0.0.0.0') { return $false }
    return (Test-VersionAtLeast -Installed $fileVersion -Minimum $minimum)
}

# Waits briefly for the state to appear (or disappear); registrations can lag a moment behind the installer.
function Wait-TargetState {
    param($Rule, [bool]$Expected, [int]$Attempts = 3, [int]$DelaySeconds = 2)
    for ($attempt = 1; $attempt -le $Attempts; $attempt++) {
        if ((Test-TargetState -Rule $Rule) -eq $Expected) { return $true }
        if ($attempt -lt $Attempts) { Start-Sleep -Seconds $DelaySeconds }
    }
    return $false
}

# ---------------------------------------------------------------- targeted post-install steps

function Get-ShortcutRoot {
    param([string]$Root)
    switch ($Root) {
        'PublicDesktop' { return [Environment]::GetFolderPath('CommonDesktopDirectory') }
        'CommonStartMenu' { return [Environment]::GetFolderPath('CommonStartMenu') }
        default { return $null }
    }
}

# Removes one shared shortcut. Only a single .lnk file below the public desktop or the common start menu is allowed:
# no user profiles, no other file types, no path that leaves the root, no links. Returns $true when nothing went wrong.
function Remove-SharedShortcut {
    param($Shortcut, [hashtable]$RootOverride)
    $rootName = [string]$Shortcut.root
    $relative = [string]$Shortcut.relativePath
    $root = $null
    if ($RootOverride -and $RootOverride.ContainsKey($rootName)) { $root = [string]$RootOverride[$rootName] }
    else { $root = Get-ShortcutRoot -Root $rootName }
    if (-not $root) {
        Write-DeployLog -Level 'ERROR' -Message "Shortcut not removed: '$rootName' is not an allowed folder."
        return $false
    }
    $segments = $relative.Split([char[]]@([char]92, [char]47))
    if ([string]::IsNullOrWhiteSpace($relative) -or [System.IO.Path]::IsPathRooted($relative) -or $relative.Contains(':') -or ($segments -contains '..') -or ($segments -contains '.') -or ($segments -contains '') -or -not $relative.EndsWith('.lnk', [System.StringComparison]::OrdinalIgnoreCase)) {
        Write-DeployLog -Level 'ERROR' -Message "Shortcut not removed: '$relative' is not a relative path to a single .lnk file."
        return $false
    }
    try {
        $rootFull = [System.IO.Path]::GetFullPath($root).TrimEnd([char]92)
        $full = [System.IO.Path]::GetFullPath((Join-Path $rootFull $relative))
        if (-not $full.StartsWith($rootFull + '\', [System.StringComparison]::OrdinalIgnoreCase)) {
            Write-DeployLog -Level 'ERROR' -Message "Shortcut not removed: '$relative' leaves the folder '$rootName'."
            return $false
        }
        if (-not (Test-Path -LiteralPath $full -PathType Leaf)) {
            Write-DeployLog -Level 'INFO' -Message "Shortcut '$relative' does not exist, nothing to remove."
            return $true
        }
        $current = $full
        while ($current.Length -gt $rootFull.Length) {
            if (((Get-Item -LiteralPath $current -Force).Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                Write-DeployLog -Level 'ERROR' -Message "Shortcut not removed: '$current' is a link."
                return $false
            }
            $current = Split-Path -Parent $current
        }
        Remove-Item -LiteralPath $full -Force
        Write-DeployLog -Level 'INFO' -Message "Removed shared shortcut '$relative' from '$rootName'."
        return $true
    }
    catch {
        Write-DeployLog -Level 'ERROR' -Message "Shortcut '$relative' could not be removed: $($_.Exception.Message)"
        return $false
    }
}

# Returns the number of failed steps. A failed step is logged visibly but does not change the result of the installation.
function Invoke-PostInstallSteps {
    param($Config, [hashtable]$RootOverride)
    $failures = 0
    foreach ($shortcut in @(Get-ConfigValue -Object $Config -Name 'sharedShortcutsToRemove' -Default @())) {
        if (-not (Remove-SharedShortcut -Shortcut $shortcut -RootOverride $RootOverride)) { $failures++ }
    }
    if ($failures -gt 0) {
        Write-DeployLog -Level 'ERROR' -Message "$failures post-install step(s) failed. The installation itself is not affected."
    }
    return $failures
}

# ---------------------------------------------------------------- user interaction

function Get-DeployMessage {
    param([string]$Language, [string]$Key)
    foreach ($name in @($Language, 'en')) {
        if (-not $script:Messages.ContainsKey($name)) {
            $file = Join-Path $PSScriptRoot "Messages.$name.psd1"
            if (-not (Test-Path -LiteralPath $file)) { continue }
            $script:Messages[$name] = Import-PowerShellDataFile -Path $file
        }
        $table = $script:Messages[$name]
        if ($table.ContainsKey($Key)) { return [string]$table[$Key] }
    }
    return $Key
}

function Initialize-WtsApi {
    if ('IpbWts' -as [type]) { return }
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public static class IpbWts
{
    [DllImport("wtsapi32.dll", EntryPoint = "WTSSendMessageW", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool WTSSendMessage(IntPtr server, int sessionId, string title, int titleLength, string message, int messageLength, int style, int timeout, out int response, bool wait);

    [DllImport("kernel32.dll")]
    public static extern uint WTSGetActiveConsoleSessionId();
}
'@
}

# Session of the user at the console, or $null when nobody is logged on (the logon screen and the service session do not count).
function Get-InteractiveSessionId {
    Initialize-WtsApi
    $id = [IpbWts]::WTSGetActiveConsoleSessionId()
    if ($id -eq [uint32]::MaxValue -or $id -eq 0) { return $null }
    $user = (Get-CimInstance -ClassName Win32_ComputerSystem -Property UserName).UserName
    if ([string]::IsNullOrWhiteSpace([string]$user)) { return $null }
    return [int]$id
}

# Shows a message box in the user's session and waits for the answer or the time limit. Returns $false when it could not be shown.
function Send-UserMessage {
    param([int]$SessionId, [string]$Title, [string]$Text, [int]$TimeoutSeconds = 60)
    Initialize-WtsApi
    $response = 0
    # MB_OK + MB_ICONINFORMATION + MB_SETFOREGROUND + MB_TOPMOST
    $style = 0x50040
    $shown = [IpbWts]::WTSSendMessage([IntPtr]::Zero, $SessionId, $Title, 2 * $Title.Length, $Text, 2 * $Text.Length, $style, $TimeoutSeconds, [ref]$response, $true)
    return [bool]$shown
}

function Get-RunningTargetProcess {
    param([string[]]$Names)
    $found = @()
    foreach ($name in $Names) {
        $base = [System.IO.Path]::GetFileNameWithoutExtension($name.Trim())
        if ($base.Length -eq 0) { continue }
        $found += @(Get-Process -Name $base -ErrorAction SilentlyContinue)
    }
    return $found
}

function Wait-ProcessesClosed {
    param([string[]]$Names, [int]$Seconds, [int]$PollSeconds = 5)
    $until = [datetime]::UtcNow.AddSeconds($Seconds)
    while ([datetime]::UtcNow -lt $until) {
        if (@(Get-RunningTargetProcess -Names $Names).Count -eq 0) { return $true }
        Start-Sleep -Seconds $PollSeconds
    }
    return (@(Get-RunningTargetProcess -Names $Names).Count -eq 0)
}

# Asks the user to close the configured programs and waits. Nothing is ended by force. Behavior without a user:
#   programs not running      -> 'Ready' (no message needed)
#   programs running, nobody logged on -> 'Retry' (Intune tries again later)
#   programs still running when the wait is over -> 'Retry'
function Request-CloseProcesses {
    param($Config, [string]$Action, [double]$MaxWaitMinutes = 30, [int]$PromptSeconds = 60, [int]$RepromptAfterSeconds = 120)
    $names = @(Get-ConfigValue -Object $Config -Name 'processesToClose' -Default @() | ForEach-Object { [string]$_ })
    if ($names.Count -eq 0) { return 'Ready' }
    if (@(Get-RunningTargetProcess -Names $names).Count -eq 0) {
        Write-DeployLog -Level 'INFO' -Message 'None of the programs to close is running.'
        return 'Ready'
    }
    $session = Get-InteractiveSessionId
    if ($null -eq $session) {
        Write-DeployLog -Level 'WARN' -Message 'Programs to close are running and nobody can be asked (no logged-on user at the console). Ending with the retry result.'
        return 'Retry'
    }
    $language = [string](Get-ConfigValue -Object $Config -Name 'language' -Default 'en')
    $software = [string](Get-ConfigValue -Object $Config -Name 'softwareName' -Default '')
    $suffix = if ($Action -eq 'Uninstall') { 'Uninstall' } else { 'Install' }
    $title = (Get-DeployMessage -Language $language -Key "CloseTitle$suffix") -f $software
    $deadline = [datetime]::UtcNow.AddMinutes($MaxWaitMinutes)
    while ($true) {
        $running = @(Get-RunningTargetProcess -Names $names)
        if ($running.Count -eq 0) { return 'Ready' }
        if ([datetime]::UtcNow -gt $deadline) {
            Write-DeployLog -Level 'WARN' -Message 'The programs were not closed in time. Ending with the retry result.'
            return 'Retry'
        }
        $list = (@($running | ForEach-Object { $_.ProcessName } | Sort-Object -Unique) -join ', ')
        $text = (Get-DeployMessage -Language $language -Key "CloseText$suffix") -f $software, $list, [Environment]::NewLine
        $extra = [string](Get-ConfigValue -Object $Config -Name 'detailMessage' -Default '')
        if ($extra.Trim().Length -gt 0) { $text = $text + [Environment]::NewLine + [Environment]::NewLine + $extra.Trim() }
        Write-DeployLog -Level 'INFO' -Message "Asking the user to close: $list"
        if (-not (Send-UserMessage -SessionId $session -Title $title -Text $text -TimeoutSeconds $PromptSeconds)) {
            Write-DeployLog -Level 'WARN' -Message 'The message could not be shown. Ending with the retry result.'
            return 'Retry'
        }
        [void](Wait-ProcessesClosed -Names $names -Seconds $RepromptAfterSeconds)
    }
}

# ---------------------------------------------------------------- the deployment

# Runs the installation or uninstallation and returns the exit code for Intune.
function Invoke-Deployment {
    [CmdletBinding()]
    param(
        [string]$DeploymentType = 'Install',
        [string]$PackageRoot,
        [string]$LogDirectory,
        [string]$ConfigFileName = 'Deployment.config.json',
        [hashtable]$ShortcutRootOverride
    )
    $script:LogFile = $null
    try {
        if ([Environment]::Is64BitOperatingSystem -and -not [Environment]::Is64BitProcess) {
            throw (New-DeployError -ExitCode $script:ExitCodes.Not64Bit -Message 'The wrapper must run in a 64-bit process.')
        }
        $config = Read-DeployConfig -Path (Join-Path $PackageRoot $ConfigFileName)
        $logs = $config.logs
        $directory = if ($LogDirectory) { $LogDirectory } else { [string]$logs.directory }
        Initialize-DeployLog -Directory $directory -FileName ([string](Get-ConfigValue -Object $logs -Name 'deploymentLog' -Default 'Deployment.log'))

        $identity = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
        Write-DeployLog -Level 'INFO' -Message ("{0} of '{1}' {2}, build {3}, user {4}, PowerShell {5}, 64-bit process {6}" -f $DeploymentType, (Get-ConfigValue -Object $config -Name 'softwareName' -Default ''), (Get-ConfigValue -Object $config -Name 'targetVersion' -Default ''), (Get-ConfigValue -Object $config -Name 'buildId' -Default ''), $identity, $PSVersionTable.PSVersion, [Environment]::Is64BitProcess)

        $timeout = [int](Get-ConfigValue -Object $config -Name 'timeoutMinutes' -Default 60)
        $limit = [Math]::Max(1, $timeout - 2)

        $ready = Request-CloseProcesses -Config $config -Action $DeploymentType -MaxWaitMinutes ([Math]::Max(1, [Math]::Min(30, [int]($timeout / 3))))
        if ($ready -ne 'Ready') {
            return [int]$script:ExitCodes.Retry
        }

        $command = New-ProcessCommand -Config $config -Action $DeploymentType -PackageRoot $PackageRoot -LogDirectory $directory
        Write-DeployLog -Level 'INFO' -Message ("Starting {0} {1}" -f $command.FilePath, $command.Arguments)
        $result = Invoke-DeployProcess -FilePath $command.FilePath -Arguments $command.Arguments -WorkingDirectory $command.WorkingDirectory -TimeoutMinutes $limit
        if ($result.TimedOut) {
            Write-DeployLog -Level 'ERROR' -Message "The process did not finish within $limit minute(s). It is not ended by force."
            return [int]$script:ExitCodes.TimedOut
        }
        $exitCode = [int]$result.ExitCode
        Write-DeployLog -Level 'INFO' -Message "The process ended with exit code $exitCode."

        $isMsi = ([string]$config.install.installerType -eq 'Msi')
        if ($DeploymentType -eq 'Uninstall' -and $isMsi -and $exitCode -eq 1605) {
            Write-DeployLog -Level 'INFO' -Message 'The product is not installed (1605). The uninstallation counts as done.'
            return 0
        }

        $type = Get-ReturnCodeType -Config $config -ExitCode $exitCode
        Write-DeployLog -Level 'INFO' -Message ("Return code type: {0}" -f $(if ($type) { $type } else { 'not configured (counts as failure)' }))
        if ($type -eq 'Success' -or $type -eq 'SoftReboot') {
            $expected = ($DeploymentType -eq 'Install')
            if (-not (Wait-TargetState -Rule $config.detection -Expected $expected)) {
                Write-DeployLog -Level 'ERROR' -Message ("The installer reported success but the target state is {0}." -f $(if ($expected) { 'not reached' } else { 'still present' }))
                return [int]$script:ExitCodes.VerificationFailed
            }
            Write-DeployLog -Level 'INFO' -Message 'The target state was verified.'
            if ($expected) {
                [void](Invoke-PostInstallSteps -Config $config -RootOverride $ShortcutRootOverride)
            }
        }
        elseif ($type -eq 'HardReboot') {
            Write-DeployLog -Level 'WARN' -Message "Exit code $exitCode means the installer already restarted the device. It is passed on as a hard reboot, never as a quiet success."
        }
        return $exitCode
    }
    catch {
        $code = [int]$script:ExitCodes.Unexpected
        if ($_.Exception.Data -and $_.Exception.Data.Contains('ExitCode')) { $code = [int]$_.Exception.Data['ExitCode'] }
        if (-not $script:LogFile) {
            # The failure happened before the configured log folder was known (for example an unreadable configuration).
            $fallback = if ($LogDirectory) { $LogDirectory } else { Join-Path ([Environment]::GetFolderPath('Windows')) 'Logs\Intune\PackageDeploy' }
            try { Initialize-DeployLog -Directory $fallback -FileName 'Wrapper-Startup.log' }
            catch { Write-Verbose 'No startup log available.' }
        }
        Write-DeployLog -Level 'ERROR' -Message ("{0} (exit code {1})" -f $_.Exception.Message, $code)
        return $code
    }
}

Export-ModuleMember -Function *
