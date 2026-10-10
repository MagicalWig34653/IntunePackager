# Behavior tests of the client runtime (deploy/template): configuration, commands, return codes, process handling,
# target state, shortcut clean-up, user interaction and complete runs (checks A15 and A16 against the wrapper).
# Windows PowerShell 5.1 and Pester 5. Real processes, registry keys and files; user interaction is mocked.

BeforeAll {
    $repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
    $script:templateDir = Join-Path $repoRoot 'deploy\template'
    Import-Module (Join-Path $script:templateDir 'DeployCore.psm1') -Force -DisableNameChecking

    $bin = Join-Path $repoRoot 'src\IntunePackageBuilder.Generation\bin\Release\net48'
    foreach ($name in 'Newtonsoft.Json', 'IntunePackageBuilder.Core', 'IntunePackageBuilder.Generation') {
        $dll = Join-Path $bin ($name + '.dll')
        if (-not (Test-Path -LiteralPath $dll)) { throw "Missing $dll. Build first: dotnet build src/IntunePackageBuilder.Generation -c Release" }
        Add-Type -Path $dll
    }

    $script:work = Join-Path ([System.IO.Path]::GetTempPath()) ('ipb-wrapper-' + [System.Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $script:work | Out-Null
    $script:kernel32 = Join-Path $env:SystemRoot 'System32\kernel32.dll'
    $script:uninstallPath = 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall'

    if (-not ('IpbTestArgv' -as [type])) {
        Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class IpbTestArgv
{
    [DllImport("shell32.dll", SetLastError = true)]
    private static extern IntPtr CommandLineToArgvW([MarshalAs(UnmanagedType.LPWStr)] string commandLine, out int count);
    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
    public static string[] Parse(string commandLine)
    {
        int count;
        IntPtr pointer = CommandLineToArgvW(commandLine, out count);
        try
        {
            string[] result = new string[count];
            for (int i = 0; i < count; i++) { result[i] = Marshal.PtrToStringUni(Marshal.ReadIntPtr(pointer, i * IntPtr.Size)); }
            return result;
        }
        finally { LocalFree(pointer); }
    }
}
'@
    }

    function New-WorkFolder {
        $path = Join-Path $script:work ([System.Guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $path | Out-Null
        return $path
    }

    function New-TestConfig {
        param([string]$Type = 'Exe', [string]$InstallerPath = 'Files\setup.cmd', [string]$Arguments = '0', $Detection = $null, $Extra = @{})
        if ($null -eq $Detection) {
            $Detection = [ordered]@{ method = 'FileVersion'; path = $script:kernel32; minimumVersion = '1.0' }
        }
        $config = [ordered]@{
            schemaVersion = 1
            buildId       = '20261008-153412-a3f9'
            language      = 'en'
            projectId     = 'test-app'
            softwareName  = 'Test App'
            manufacturer  = 'Contoso'
            targetVersion = '1.0.0'
            install       = [ordered]@{ installerType = $Type; installerPath = $InstallerPath; arguments = $Arguments; targetArchitecture = 'X64' }
            uninstall     = [ordered]@{ executablePath = '%PACKAGE%\Files\uninstall.cmd'; arguments = '0' }
            detection     = $Detection
            timeoutMinutes = 10
            returnCodes   = @(
                [ordered]@{ code = 0; type = 'Success' },
                [ordered]@{ code = 1618; type = 'Retry' },
                [ordered]@{ code = 1641; type = 'HardReboot' },
                [ordered]@{ code = 3010; type = 'SoftReboot' }
            )
            logs          = [ordered]@{ directory = 'C:\unused'; deploymentLog = 'Deployment.log'; msiInstallLog = 'MsiInstall.log'; msiUninstallLog = 'MsiUninstall.log' }
        }
        foreach ($key in $Extra.Keys) { $config[$key] = $Extra[$key] }
        return ($config | ConvertTo-Json -Depth 8 | ConvertFrom-Json)
    }

    # A package folder with the templates, a configuration and batch files that act as installer and uninstaller.
    function New-TestPackage {
        param($Config, [string]$Uninstaller = $null)
        $root = New-WorkFolder
        Copy-Item -Path (Join-Path $script:templateDir '*') -Destination $root -Recurse
        New-Item -ItemType Directory -Path (Join-Path $root 'Files') | Out-Null
        Set-Content -LiteralPath (Join-Path $root 'Files\setup.cmd') -Value "@echo off`r`nexit /b %1" -Encoding Ascii
        Set-Content -LiteralPath (Join-Path $root 'Files\uninstall.cmd') -Value "@echo off`r`nexit /b %1" -Encoding Ascii
        $json = $Config | ConvertTo-Json -Depth 8
        $json = $json.Replace('%PACKAGE%', ($root -replace '\\', '\\'))
        [System.IO.File]::WriteAllText((Join-Path $root 'Deployment.config.json'), $json, (New-Object System.Text.UTF8Encoding($true)))
        return $root
    }

    function Set-UninstallEntry {
        param([string]$ProductCode, [string]$DisplayVersion, [Microsoft.Win32.RegistryView]$View)
        $base = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine, $View)
        try {
            $key = $base.CreateSubKey($script:uninstallPath + '\' + $ProductCode)
            try { $key.SetValue('DisplayVersion', $DisplayVersion) } finally { $key.Dispose() }
        }
        finally { $base.Dispose() }
    }

    function Remove-UninstallEntry {
        param([string]$ProductCode)
        foreach ($view in [Microsoft.Win32.RegistryView]::Registry64, [Microsoft.Win32.RegistryView]::Registry32) {
            $base = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine, $view)
            try { $base.DeleteSubKeyTree($script:uninstallPath + '\' + $ProductCode, $false) } finally { $base.Dispose() }
        }
    }

    function Get-ExitCodeOfFailure {
        param([scriptblock]$Action)
        try { & $Action; return $null }
        catch { return [int]$_.Exception.Data['ExitCode'] }
    }
}

AfterAll {
    Remove-Item -Recurse -Force $script:work -ErrorAction SilentlyContinue
}

Describe 'Package templates' {
    It 'has an ASCII entry script that starts the native 64-bit PowerShell with the arguments' {
        $bytes = [System.IO.File]::ReadAllBytes((Join-Path $script:templateDir 'Install.cmd'))
        ($bytes | Where-Object { $_ -gt 127 }) | Should -BeNullOrEmpty
        $text = [System.Text.Encoding]::ASCII.GetString($bytes)
        $text | Should -Match 'Sysnative'
        $text | Should -Match '-File "%WRAPPER%" %\*'
        $text | Should -Match 'exit /b %ERRORLEVEL%'
    }

    It 'has the same message keys in German and English' {
        $en = Import-PowerShellDataFile -Path (Join-Path $script:templateDir 'Messages.en.psd1')
        $de = Import-PowerShellDataFile -Path (Join-Path $script:templateDir 'Messages.de.psd1')
        @($en.Keys | Sort-Object) | Should -Be @($de.Keys | Sort-Object)
        foreach ($key in $en.Keys) {
            ([regex]::Matches($en[$key], '\{\d\}') | ForEach-Object { $_.Value } | Sort-Object) | Should -Be ([regex]::Matches($de[$key], '\{\d\}') | ForEach-Object { $_.Value } | Sort-Object)
        }
    }

    It 'provides every file name the build interface promises' {
        foreach ($name in 'Install.cmd', 'Deploy-Wrapper.ps1', 'DeployCore.psm1') {
            Test-Path -LiteralPath (Join-Path $script:templateDir $name) | Should -BeTrue
        }
        $interface = [IntunePackageBuilder.Generation.Intune.DeploymentInterface]
        $interface::EntryScript | Should -Be 'Install.cmd'
        $interface::WrapperConfigFile | Should -Be 'Deployment.config.json'
    }
}

Describe 'ConvertTo-ArgumentString' {
    It 'is read back exactly by the Windows parser: <Text>' -ForEach @(
        @{ Text = 'plain' }, @{ Text = '' }, @{ Text = 'C:\Program Files\App' }, @{ Text = 'C:\my dir\' },
        @{ Text = 'a"b' }, @{ Text = 'a\"b' }, @{ Text = 'trailing\\' }, @{ Text = '"quoted"' }, @{ Text = 'x & y | z' }
    ) {
        $line = ConvertTo-ArgumentString -Arguments @('program.exe', 'first', $Text, 'last')
        $parsed = [IpbTestArgv]::Parse($line)
        $parsed.Count | Should -Be 4
        $parsed[2] | Should -Be $Text
        $parsed[3] | Should -Be 'last'
    }
}

Describe 'Read-DeployConfig' {
    It 'reads a valid configuration, also with a BOM' {
        $root = New-TestPackage -Config (New-TestConfig)
        $config = Read-DeployConfig -Path (Join-Path $root 'Deployment.config.json')
        $config.softwareName | Should -Be 'Test App'
        @($config.returnCodes).Count | Should -Be 4
    }

    It 'reports <Case> with the configuration error code' -ForEach @(
        @{ Case = 'a missing file'; Content = $null },
        @{ Case = 'invalid JSON'; Content = '{ not json' },
        @{ Case = 'an unknown schema version'; Content = '{ "schemaVersion": 99 }' },
        @{ Case = 'a missing section'; Content = '{ "schemaVersion": 1, "install": {} }' }
    ) {
        $folder = New-WorkFolder
        $path = Join-Path $folder 'Deployment.config.json'
        if ($null -ne $Content) { Set-Content -LiteralPath $path -Value $Content -Encoding UTF8 }
        Get-ExitCodeOfFailure { Read-DeployConfig -Path $path } | Should -Be 60002
    }
}

Describe 'Get-ReturnCodeType' {
    It 'classifies <Code> as <Type>' -ForEach @(
        @{ Code = 0; Type = 'Success' }, @{ Code = 3010; Type = 'SoftReboot' }, @{ Code = 1618; Type = 'Retry' },
        @{ Code = 1641; Type = 'HardReboot' }, @{ Code = 5; Type = $null }
    ) {
        $actual = Get-ReturnCodeType -Config (New-TestConfig) -ExitCode $Code
        if ($null -eq $Type) { $actual | Should -BeNullOrEmpty } else { $actual | Should -Be $Type }
    }
}

Describe 'Get-ProcessTimeLimit' {
    It 'leaves the time limit minus the margin when nothing was used yet' {
        $limit = Get-ProcessTimeLimit -TimeoutMinutes 60 -StartedUtc ([datetime]::UtcNow)
        $limit | Should -BeGreaterThan 57.9
        $limit | Should -BeLessOrEqual 58
    }

    It 'subtracts the time already used, for example waiting for programs to close' {
        $limit = Get-ProcessTimeLimit -TimeoutMinutes 60 -StartedUtc ([datetime]::UtcNow.AddMinutes(-20))
        $limit | Should -BeGreaterThan 37.5
        $limit | Should -BeLessOrEqual 38
    }

    It 'never goes below one minute' {
        Get-ProcessTimeLimit -TimeoutMinutes 5 -StartedUtc ([datetime]::UtcNow.AddMinutes(-30)) | Should -Be 1
    }
}

Describe 'New-ProcessCommand' {
    It 'builds the silent MSI installation with a log and appends configured properties' {
        $config = New-TestConfig -Type 'Msi' -InstallerPath 'Files\setup.msi' -Arguments 'ALLUSERS=1 INSTALLDIR="C:\Apps\X"'
        $root = New-WorkFolder
        New-Item -ItemType Directory -Path (Join-Path $root 'Files') | Out-Null
        Set-Content -LiteralPath (Join-Path $root 'Files\setup.msi') -Value 'x'
        $command = New-ProcessCommand -Config $config -Action 'Install' -PackageRoot $root -LogDirectory 'C:\Logs\x'
        $command.FilePath | Should -Match 'msiexec\.exe$'
        $parsed = [IpbTestArgv]::Parse('msiexec ' + $command.Arguments)
        $parsed[1] | Should -Be '/i'
        $parsed[2] | Should -Be (Join-Path $root 'Files\setup.msi')
        $parsed | Should -Contain '/qn'
        $parsed | Should -Contain '/norestart'
        $parsed | Should -Contain '/l*v'
        $parsed | Should -Contain 'C:\Logs\x\MsiInstall.log'
        $parsed | Should -Contain 'ALLUSERS=1'
    }

    It 'uses the uninstall product code, or the install product code when there is none' {
        $config = New-TestConfig -Type 'Msi' -InstallerPath 'Files\setup.msi'
        $config.install | Add-Member -NotePropertyName productCode -NotePropertyValue '{8F2C41A7-5B3E-4D19-A7C0-3E6D21B94F05}'
        $command = New-ProcessCommand -Config $config -Action 'Uninstall' -PackageRoot (New-WorkFolder) -LogDirectory 'C:\Logs\x'
        $parsed = [IpbTestArgv]::Parse('msiexec ' + $command.Arguments)
        $parsed[1] | Should -Be '/x'
        $parsed[2] | Should -Be '{8F2C41A7-5B3E-4D19-A7C0-3E6D21B94F05}'
        $parsed | Should -Contain 'C:\Logs\x\MsiUninstall.log'
    }

    It 'rejects an uninstall without a valid product code' {
        $config = New-TestConfig -Type 'Msi' -InstallerPath 'Files\setup.msi'
        Get-ExitCodeOfFailure { New-ProcessCommand -Config $config -Action 'Uninstall' -PackageRoot (New-WorkFolder) -LogDirectory 'C:\x' } | Should -Be 60002
    }

    It 'passes the EXE arguments exactly as configured and invents nothing' {
        $root = New-TestPackage -Config (New-TestConfig -Arguments '/quiet /norestart')
        $config = Read-DeployConfig -Path (Join-Path $root 'Deployment.config.json')
        $command = New-ProcessCommand -Config $config -Action 'Install' -PackageRoot $root -LogDirectory 'C:\x'
        $command.FilePath | Should -Be (Join-Path $root 'Files\setup.cmd')
        $command.Arguments | Should -Be '/quiet /norestart'
        $command.WorkingDirectory | Should -Be (Join-Path $root 'Files')
    }

    It 'reports a missing installer with its own error code' {
        $config = New-TestConfig
        Get-ExitCodeOfFailure { New-ProcessCommand -Config $config -Action 'Install' -PackageRoot (New-WorkFolder) -LogDirectory 'C:\x' } | Should -Be 60003
    }

    It 'reports an EXE uninstall without a program as a configuration error' {
        $config = New-TestConfig
        $config.uninstall.executablePath = ''
        Get-ExitCodeOfFailure { New-ProcessCommand -Config $config -Action 'Uninstall' -PackageRoot (New-WorkFolder) -LogDirectory 'C:\x' } | Should -Be 60002
    }
}

Describe 'Invoke-DeployProcess' {
    BeforeAll { $script:cmd = Join-Path $env:SystemRoot 'System32\cmd.exe' }

    It 'returns the exit code of the process' {
        $result = Invoke-DeployProcess -FilePath $script:cmd -Arguments '/c exit 7' -WorkingDirectory $null -TimeoutMinutes 1
        $result.TimedOut | Should -BeFalse
        $result.ExitCode | Should -Be 7
    }

    It 'waits for a process the installer started and left running (a bootstrapper that ends early)' {
        $watch = [System.Diagnostics.Stopwatch]::StartNew()
        $result = Invoke-DeployProcess -FilePath $script:cmd -Arguments '/c start /b ping -n 5 127.0.0.1 >nul & exit /b 3' -WorkingDirectory $null -TimeoutMinutes 1
        $watch.Stop()
        $result.ExitCode | Should -Be 3
        $watch.Elapsed.TotalSeconds | Should -BeGreaterThan 2.5
    }

    It 'reports the time limit and does not end the process by force' {
        $result = Invoke-DeployProcess -FilePath $script:cmd -Arguments '/c ping -n 8 127.0.0.1 >nul' -WorkingDirectory $null -TimeoutMinutes 0.03
        $result.TimedOut | Should -BeTrue
        $result.ExitCode | Should -BeNullOrEmpty
    }
}

Describe 'Test-TargetState compared with the generated detection script (A15)' {
    BeforeEach { $script:code = '{' + [System.Guid]::NewGuid().ToString().ToUpperInvariant() + '}' }
    AfterEach { Remove-UninstallEntry -ProductCode $script:code }

    It 'agrees with the script for installed <Installed> and minimum <Minimum>' -ForEach @(
        @{ Installed = '4.1.9'; Minimum = '4.2.1' }, @{ Installed = '4.2.1'; Minimum = '4.2.1' }, @{ Installed = '4.2.2'; Minimum = '4.2.1' },
        @{ Installed = '1.10'; Minimum = '1.9' }, @{ Installed = '4.2.1-beta'; Minimum = '4.2.1' }, @{ Installed = 'unknown'; Minimum = '4.2.1' },
        @{ Installed = '4.2.1.7.9'; Minimum = '4.2.1' }, @{ Installed = ''; Minimum = '1.0' }
    ) {
        Set-UninstallEntry -ProductCode $script:code -DisplayVersion $Installed -View ([Microsoft.Win32.RegistryView]::Registry64)
        $rule = [pscustomobject]@{ method = 'MsiProductCode'; productCode = $script:code; minimumVersion = $Minimum }
        $scriptText = [IntunePackageBuilder.Generation.Scripts.DetectionScriptGenerator]::GenerateForMsi('20261008-153412-a3f9', $script:code, $Minimum)
        $file = Join-Path (New-WorkFolder) 'Detect-App.ps1'
        [IntunePackageBuilder.Generation.Scripts.DetectionScriptGenerator]::WriteTo($file, $scriptText)
        & powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File $file 2>$null | Out-Null
        $detected = ($LASTEXITCODE -eq 0)
        Test-TargetState -Rule $rule | Should -Be $detected
    }

    It 'finds an installation in the 32-bit registry view and none when the entry is absent' {
        $rule = [pscustomobject]@{ method = 'MsiProductCode'; productCode = $script:code; minimumVersion = '1.0' }
        Test-TargetState -Rule $rule | Should -BeFalse
        Set-UninstallEntry -ProductCode $script:code -DisplayVersion '1.0' -View ([Microsoft.Win32.RegistryView]::Registry32)
        Test-TargetState -Rule $rule | Should -BeTrue
    }

    It 'checks the file version of a file' {
        Test-TargetState -Rule ([pscustomobject]@{ method = 'FileVersion'; path = $script:kernel32; minimumVersion = '1.0' }) | Should -BeTrue
        Test-TargetState -Rule ([pscustomobject]@{ method = 'FileVersion'; path = $script:kernel32; minimumVersion = '999.0' }) | Should -BeFalse
        Test-TargetState -Rule ([pscustomobject]@{ method = 'FileVersion'; path = (Join-Path $script:work 'missing.exe'); minimumVersion = '1.0' }) | Should -BeFalse
    }
}

Describe 'Remove-SharedShortcut' {
    BeforeEach {
        $script:roots = @{ PublicDesktop = (New-WorkFolder); CommonStartMenu = (New-WorkFolder) }
        Initialize-DeployLog -Directory (New-WorkFolder) -FileName 'test.log'
    }

    It 'removes a single .lnk file in an allowed folder' {
        $path = Join-Path $script:roots.PublicDesktop 'App.lnk'
        Set-Content -LiteralPath $path -Value 'x'
        Remove-SharedShortcut -Shortcut ([pscustomobject]@{ root = 'PublicDesktop'; relativePath = 'App.lnk' }) -RootOverride $script:roots | Should -BeTrue
        Test-Path -LiteralPath $path | Should -BeFalse
    }

    It 'removes a shortcut in a subfolder of the start menu' {
        $folder = Join-Path $script:roots.CommonStartMenu 'Contoso'
        New-Item -ItemType Directory -Path $folder | Out-Null
        Set-Content -LiteralPath (Join-Path $folder 'App.lnk') -Value 'x'
        Remove-SharedShortcut -Shortcut ([pscustomobject]@{ root = 'CommonStartMenu'; relativePath = 'Contoso/App.lnk' }) -RootOverride $script:roots | Should -BeTrue
        Test-Path -LiteralPath (Join-Path $folder 'App.lnk') | Should -BeFalse
    }

    It 'treats a shortcut that does not exist as nothing to do' {
        Remove-SharedShortcut -Shortcut ([pscustomobject]@{ root = 'PublicDesktop'; relativePath = 'Absent.lnk' }) -RootOverride $script:roots | Should -BeTrue
    }

    It 'refuses <Case> and leaves the file alone' -ForEach @(
        @{ Case = 'a path that leaves the folder'; Root = 'PublicDesktop'; Relative = '..\Escape.lnk' },
        @{ Case = 'an absolute path'; Root = 'PublicDesktop'; Relative = 'C:\Windows\Evil.lnk' },
        @{ Case = 'a file that is not a shortcut'; Root = 'PublicDesktop'; Relative = 'Document.txt' },
        @{ Case = 'a folder that is not allowed'; Root = 'UserProfile'; Relative = 'App.lnk' },
        @{ Case = 'an empty path'; Root = 'PublicDesktop'; Relative = '' }
    ) {
        $outside = Join-Path (Split-Path -Parent $script:roots.PublicDesktop) 'Escape.lnk'
        Set-Content -LiteralPath $outside -Value 'keep'
        Set-Content -LiteralPath (Join-Path $script:roots.PublicDesktop 'Document.txt') -Value 'keep'
        Remove-SharedShortcut -Shortcut ([pscustomobject]@{ root = $Root; relativePath = $Relative }) -RootOverride $script:roots | Should -BeFalse
        Test-Path -LiteralPath $outside | Should -BeTrue
        Test-Path -LiteralPath (Join-Path $script:roots.PublicDesktop 'Document.txt') | Should -BeTrue
    }

    It 'refuses to follow a junction out of the allowed folder' {
        $target = New-WorkFolder
        Set-Content -LiteralPath (Join-Path $target 'Victim.lnk') -Value 'keep'
        $link = Join-Path $script:roots.PublicDesktop 'Link'
        & cmd.exe /c mklink /J "$link" "$target" | Out-Null
        Remove-SharedShortcut -Shortcut ([pscustomobject]@{ root = 'PublicDesktop'; relativePath = 'Link/Victim.lnk' }) -RootOverride $script:roots | Should -BeFalse
        Test-Path -LiteralPath (Join-Path $target 'Victim.lnk') | Should -BeTrue
    }

    It 'counts failed steps and logs them without throwing' {
        $config = [pscustomobject]@{ sharedShortcutsToRemove = @(
                [pscustomobject]@{ root = 'PublicDesktop'; relativePath = '..\x.lnk' },
                [pscustomobject]@{ root = 'PublicDesktop'; relativePath = 'Absent.lnk' }) }
        Invoke-PostInstallSteps -Config $config -RootOverride $script:roots | Should -Be 1
    }
}

Describe 'Request-CloseProcesses' {
    BeforeEach {
        Initialize-DeployLog -Directory (New-WorkFolder) -FileName 'test.log'
        $script:config = New-TestConfig -Extra @{ processesToClose = @('reader.exe', 'viewer'); language = 'de' }
    }

    It 'needs nothing when no programs are configured' {
        Request-CloseProcesses -Config (New-TestConfig) -Action 'Install' | Should -Be 'Ready'
    }

    It 'needs nothing when the configured programs are not running' {
        Mock -ModuleName DeployCore Get-RunningTargetProcess { @() }
        Mock -ModuleName DeployCore Send-UserMessage { $true }
        Request-CloseProcesses -Config $script:config -Action 'Install' | Should -Be 'Ready'
        Should -Invoke -ModuleName DeployCore Send-UserMessage -Times 0 -Exactly
    }

    It 'ends with the retry result when programs are running and nobody is logged on' {
        Mock -ModuleName DeployCore Get-RunningTargetProcess { [pscustomobject]@{ ProcessName = 'reader' } }
        Mock -ModuleName DeployCore Get-InteractiveSessionId { $null }
        Mock -ModuleName DeployCore Send-UserMessage { $true }
        Request-CloseProcesses -Config $script:config -Action 'Install' | Should -Be 'Retry'
        Should -Invoke -ModuleName DeployCore Send-UserMessage -Times 0 -Exactly
    }

    It 'asks the user in the language of the build and continues once the programs are closed' {
        # Mocks run in the module scope, so the state they share lives in a global variable.
        $global:IpbTestMessageShown = $false
        Mock -ModuleName DeployCore Get-RunningTargetProcess { if (-not $global:IpbTestMessageShown) { [pscustomobject]@{ ProcessName = 'reader' } } else { @() } }
        Mock -ModuleName DeployCore Get-InteractiveSessionId { 3 }
        Mock -ModuleName DeployCore Send-UserMessage { $global:IpbTestMessageShown = $true; $true }
        Request-CloseProcesses -Config $script:config -Action 'Install' -RepromptAfterSeconds 0 | Should -Be 'Ready'
        Should -Invoke -ModuleName DeployCore Send-UserMessage -Times 1 -Exactly -ParameterFilter { $SessionId -eq 3 -and $Title -like 'Installation von Test App' -and $Text -like '*reader*' -and $Text -like '*Bitte speichern*' }
    }

    It 'ends with the retry result when the programs stay open until the wait is over' {
        Mock -ModuleName DeployCore Get-RunningTargetProcess { [pscustomobject]@{ ProcessName = 'reader' } }
        Mock -ModuleName DeployCore Get-InteractiveSessionId { 3 }
        Mock -ModuleName DeployCore Send-UserMessage { $true }
        Request-CloseProcesses -Config $script:config -Action 'Install' -MaxWaitMinutes 0.01 -RepromptAfterSeconds 0 | Should -Be 'Retry'
    }

    It 'ends with the retry result when the message cannot be shown' {
        Mock -ModuleName DeployCore Get-RunningTargetProcess { [pscustomobject]@{ ProcessName = 'reader' } }
        Mock -ModuleName DeployCore Get-InteractiveSessionId { 3 }
        Mock -ModuleName DeployCore Send-UserMessage { $false }
        Request-CloseProcesses -Config $script:config -Action 'Uninstall' | Should -Be 'Retry'
    }
}

Describe 'Invoke-Deployment (A15, A16 against the wrapper)' {
    BeforeEach { $script:logDir = New-WorkFolder }

    It 'passes success on and verifies the target state: exit code <Code> gives <Expected>' -ForEach @(
        @{ Code = '0'; Expected = 0 }, @{ Code = '3010'; Expected = 3010 }
    ) {
        $root = New-TestPackage -Config (New-TestConfig -Arguments $Code)
        Invoke-Deployment -DeploymentType 'Install' -PackageRoot $root -LogDirectory $script:logDir | Should -Be $Expected
        Get-Content -Raw -LiteralPath (Join-Path $script:logDir 'Deployment.log') | Should -Match 'target state was verified'
    }

    It 'passes the retry result on without verifying anything' {
        $root = New-TestPackage -Config (New-TestConfig -Arguments '1618' -Detection ([ordered]@{ method = 'FileVersion'; path = (Join-Path $script:work 'missing.exe'); minimumVersion = '1.0' }))
        Invoke-Deployment -DeploymentType 'Install' -PackageRoot $root -LogDirectory $script:logDir | Should -Be 1618
    }

    It 'never treats 1641 as a quiet success' {
        $root = New-TestPackage -Config (New-TestConfig -Arguments '1641')
        Invoke-Deployment -DeploymentType 'Install' -PackageRoot $root -LogDirectory $script:logDir | Should -Be 1641
        Get-Content -Raw -LiteralPath (Join-Path $script:logDir 'Deployment.log') | Should -Match 'never as a quiet success'
    }

    It 'passes an unconfigured exit code on as the failure it is' {
        $root = New-TestPackage -Config (New-TestConfig -Arguments '5')
        Invoke-Deployment -DeploymentType 'Install' -PackageRoot $root -LogDirectory $script:logDir | Should -Be 5
    }

    It 'does not accept success when the target state was not reached' {
        $missing = [ordered]@{ method = 'FileVersion'; path = (Join-Path $script:work 'never-installed.exe'); minimumVersion = '1.0' }
        $root = New-TestPackage -Config (New-TestConfig -Arguments '0' -Detection $missing)
        Invoke-Deployment -DeploymentType 'Install' -PackageRoot $root -LogDirectory $script:logDir | Should -Be 60001
        Get-Content -Raw -LiteralPath (Join-Path $script:logDir 'Deployment.log') | Should -Match 'not reached'
    }

    It 'reports a missing installer and an unreadable configuration with their own codes' {
        $root = New-TestPackage -Config (New-TestConfig)
        Remove-Item -LiteralPath (Join-Path $root 'Files\setup.cmd')
        Invoke-Deployment -DeploymentType 'Install' -PackageRoot $root -LogDirectory $script:logDir | Should -Be 60003
        Invoke-Deployment -DeploymentType 'Install' -PackageRoot (New-WorkFolder) -LogDirectory $script:logDir | Should -Be 60002
    }

    It 'verifies after an uninstallation that the target state is gone' {
        $present = [ordered]@{ method = 'FileVersion'; path = $script:kernel32; minimumVersion = '1.0' }
        $root = New-TestPackage -Config (New-TestConfig -Detection $present)
        Invoke-Deployment -DeploymentType 'Uninstall' -PackageRoot $root -LogDirectory $script:logDir | Should -Be 60001

        $gone = [ordered]@{ method = 'FileVersion'; path = (Join-Path $script:work 'gone.exe'); minimumVersion = '1.0' }
        $root = New-TestPackage -Config (New-TestConfig -Detection $gone)
        Invoke-Deployment -DeploymentType 'Uninstall' -PackageRoot $root -LogDirectory $script:logDir | Should -Be 0
    }

    It 'counts an MSI uninstallation of a product that is not installed as done (real msiexec)' {
        $code = '{' + [System.Guid]::NewGuid().ToString().ToUpperInvariant() + '}'
        $gone = [ordered]@{ method = 'MsiProductCode'; productCode = $code; minimumVersion = '1.0' }
        $config = New-TestConfig -Type 'Msi' -InstallerPath 'Files\setup.msi' -Detection $gone
        $config.install | Add-Member -NotePropertyName productCode -NotePropertyValue $code
        $root = New-TestPackage -Config $config
        Invoke-Deployment -DeploymentType 'Uninstall' -PackageRoot $root -LogDirectory $script:logDir | Should -Be 0
        Get-Content -Raw -LiteralPath (Join-Path $script:logDir 'Deployment.log') | Should -Match '1605'
    }

    It 'removes the configured shared shortcuts after a verified installation' {
        $roots = @{ PublicDesktop = (New-WorkFolder) }
        Set-Content -LiteralPath (Join-Path $roots.PublicDesktop 'App.lnk') -Value 'x'
        $config = New-TestConfig -Extra @{ sharedShortcutsToRemove = @([ordered]@{ root = 'PublicDesktop'; relativePath = 'App.lnk' }) }
        $root = New-TestPackage -Config $config
        Invoke-Deployment -DeploymentType 'Install' -PackageRoot $root -LogDirectory $script:logDir -ShortcutRootOverride $roots | Should -Be 0
        Test-Path -LiteralPath (Join-Path $roots.PublicDesktop 'App.lnk') | Should -BeFalse
    }

    It 'keeps the result of the installation when a post-install step fails' {
        $config = New-TestConfig -Extra @{ sharedShortcutsToRemove = @([ordered]@{ root = 'PublicDesktop'; relativePath = '..\x.lnk' }) }
        $root = New-TestPackage -Config $config
        Invoke-Deployment -DeploymentType 'Install' -PackageRoot $root -LogDirectory $script:logDir -ShortcutRootOverride @{ PublicDesktop = (New-WorkFolder) } | Should -Be 0
        Get-Content -Raw -LiteralPath (Join-Path $script:logDir 'Deployment.log') | Should -Match 'post-install step\(s\) failed'
    }

    It 'asks to retry later when programs are open and nobody can be asked' {
        Mock -ModuleName DeployCore Get-RunningTargetProcess { [pscustomobject]@{ ProcessName = 'reader' } }
        Mock -ModuleName DeployCore Get-InteractiveSessionId { $null }
        $root = New-TestPackage -Config (New-TestConfig -Extra @{ processesToClose = @('reader.exe') })
        Invoke-Deployment -DeploymentType 'Install' -PackageRoot $root -LogDirectory $script:logDir | Should -Be 1618
    }

    It 'lets an interaction provider replace the question to the user and ends with the retry result without starting the installer' {
        $root = New-TestPackage -Config (New-TestConfig -Arguments '0')
        $script:seen = $null
        $interaction = @{ RequestClose = { param($Config, $Action, $MaxWaitMinutes) $script:seen = @($Config.softwareName, $Action, $MaxWaitMinutes); 'Retry' } }
        Invoke-Deployment -DeploymentType 'Uninstall' -PackageRoot $root -LogDirectory $script:logDir -Interaction $interaction | Should -Be 1618
        $script:seen[0] | Should -Be 'Test App'
        $script:seen[1] | Should -Be 'Uninstall'
        $script:seen[2] | Should -Be 3
        Get-Content -Raw -LiteralPath (Join-Path $script:logDir 'Deployment.log') | Should -Not -Match 'Starting '
    }

    It 'calls the progress hook once, right before the installer starts, and keeps the normal result' {
        $root = New-TestPackage -Config (New-TestConfig -Arguments '0')
        $script:progress = 0
        $interaction = @{ RequestClose = { 'Ready' }; ShowProgress = { param($Config, $Action) $script:progress++ } }
        Invoke-Deployment -DeploymentType 'Install' -PackageRoot $root -LogDirectory $script:logDir -Interaction $interaction | Should -Be 0
        $script:progress | Should -Be 1
    }

    It 'behaves as before when the provider has no hooks' {
        $root = New-TestPackage -Config (New-TestConfig -Arguments '0')
        Invoke-Deployment -DeploymentType 'Install' -PackageRoot $root -LogDirectory $script:logDir -Interaction @{} | Should -Be 0
    }
}

Describe 'Install.cmd runs the whole chain in a separate Windows PowerShell process' {
    It 'installs and writes the log: exit code <Code>' -ForEach @(@{ Code = '0' }, @{ Code = '3010' }, @{ Code = '5' }) {
        $logDir = New-WorkFolder
        $root = New-TestPackage -Config (New-TestConfig -Arguments $Code)
        & "$root\Install.cmd" -LogDirectory $logDir | Out-Null
        $LASTEXITCODE | Should -Be ([int]$Code)
        Test-Path -LiteralPath (Join-Path $logDir 'Deployment.log') | Should -BeTrue
    }

    It 'passes -DeploymentType Uninstall to the wrapper' {
        $logDir = New-WorkFolder
        $gone = [ordered]@{ method = 'FileVersion'; path = (Join-Path $script:work 'gone.exe'); minimumVersion = '1.0' }
        $root = New-TestPackage -Config (New-TestConfig -Detection $gone)
        & "$root\Install.cmd" -DeploymentType Uninstall -LogDirectory $logDir | Out-Null
        $LASTEXITCODE | Should -Be 0
        Get-Content -Raw -LiteralPath (Join-Path $logDir 'Deployment.log') | Should -Match 'Uninstall of'
    }

    It 'returns the configuration error code when the configuration is missing' {
        $root = New-WorkFolder
        Copy-Item -Path (Join-Path $script:templateDir '*') -Destination $root -Recurse
        $logDir = New-WorkFolder
        & "$root\Install.cmd" -LogDirectory $logDir | Out-Null
        $LASTEXITCODE | Should -Be 60002
    }
}

Describe 'Deployment log' {
    It 'restricts the log folder to SYSTEM and administrators' -Skip:(-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        $directory = Join-Path (New-WorkFolder) 'logs'
        Initialize-DeployLog -Directory $directory -FileName 'Deployment.log'
        Write-DeployLog -Level 'INFO' -Message 'hello'
        $acl = Get-Acl -LiteralPath $directory
        $acl.AreAccessRulesProtected | Should -BeTrue
        $sids = @($acl.Access | ForEach-Object { $_.IdentityReference.Translate([System.Security.Principal.SecurityIdentifier]).Value } | Sort-Object -Unique)
        $sids | Should -Be @('S-1-5-18', 'S-1-5-32-544')
        Get-Content -Raw -LiteralPath (Join-Path $directory 'Deployment.log') | Should -Match 'INFO  hello'
    }
}
