# The PSAppDeployToolkit engine against the real toolkit (docs/PLANUNG.md section 8, number 12).
# The toolkit is not part of the repository: CI downloads the pinned release and sets IPB_PSADT_ZIP; without it these tests are skipped.
# Everything the program generates (configuration, overlay files, strings) comes from the real generators; the entry script is the real template.
# Windows PowerShell 5.1 and Pester 5. Dialogs are never shown: the entry script runs in Silent mode.

BeforeDiscovery {
    $script:hasToolkit = -not [string]::IsNullOrWhiteSpace($env:IPB_PSADT_ZIP) -and (Test-Path -LiteralPath $env:IPB_PSADT_ZIP)

    $script:welcomeCombinations = @((Import-PowerShellDataFile -Path (Join-Path $PSScriptRoot 'PsadtWelcomeCombinations.psd1')).Combinations)
}

BeforeAll {
    $script:repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
    $bin = Join-Path $script:repoRoot 'src\IntunePackageBuilder.Generation\bin\Release\net48'
    foreach ($name in 'Newtonsoft.Json', 'IntunePackageBuilder.Core', 'IntunePackageBuilder.Generation') {
        $dll = Join-Path $bin ($name + '.dll')
        if (-not (Test-Path -LiteralPath $dll)) { throw "Missing $dll. Build first: dotnet build src/IntunePackageBuilder.Generation -c Release" }
        Add-Type -Path $dll
    }

    $script:welcomeCombinations = @((Import-PowerShellDataFile -Path (Join-Path $PSScriptRoot 'PsadtWelcomeCombinations.psd1')).Combinations)
    $script:work = Join-Path ([System.IO.Path]::GetTempPath()) ('ipb-psadt-' + [System.Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $script:work | Out-Null
    $script:entryTemplate = Join-Path $script:repoRoot 'deploy\psadt\template'
    $script:coreTemplate = Join-Path $script:repoRoot 'deploy\template'
    $script:kernel32 = Join-Path $env:SystemRoot 'System32\kernel32.dll'
    $script:logFolders = New-Object System.Collections.Generic.List[string]

    if ($env:IPB_PSADT_ZIP -and (Test-Path -LiteralPath $env:IPB_PSADT_ZIP)) {
        # The module as the program copies it: the folder PSAppDeployToolkit from the release ZIP, without the old front end.
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $script:moduleParent = Join-Path $script:work 'module'
        New-Item -ItemType Directory -Path $script:moduleParent | Out-Null
        $archive = [System.IO.Compression.ZipFile]::OpenRead($env:IPB_PSADT_ZIP)
        try {
            foreach ($entry in $archive.Entries) {
                $name = $entry.FullName.Replace('\', '/')
                if (-not $name.StartsWith('PSAppDeployToolkit/') -or $name.EndsWith('/') -or $name.StartsWith('PSAppDeployToolkit/Frontend/') -or $name.EndsWith('.pdb')) { continue }
                $target = Join-Path $script:moduleParent $name.Replace('/', '\')
                New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
                [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $target)
            }
        }
        finally { $archive.Dispose() }
        $script:moduleManifest = Join-Path $script:moduleParent 'PSAppDeployToolkit\PSAppDeployToolkit.psd1'
    }

    # A complete package as the program builds it: shared runtime, toolkit entry script, toolkit module, generated configuration and overlay.
    function New-ToolkitPackage {
        param([scriptblock]$Configure = {}, [string]$Language = 'en', [string]$InstallerExitCode = '0', [string]$DetectionPath = $script:kernel32)
        $root = Join-Path $script:work ([System.Guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $root | Out-Null
        foreach ($name in 'DeployCore.psm1', 'Messages.de.psd1', 'Messages.en.psd1') { Copy-Item -LiteralPath (Join-Path $script:coreTemplate $name) -Destination $root }
        Copy-Item -Path (Join-Path $script:entryTemplate '*') -Destination $root
        Copy-Item -LiteralPath (Join-Path $script:moduleParent 'PSAppDeployToolkit') -Destination $root -Recurse
        New-Item -ItemType Directory -Path (Join-Path $root 'Files') | Out-Null
        Set-Content -LiteralPath (Join-Path $root 'Files\setup.cmd') -Value "@echo off`r`nexit /b %1" -Encoding Ascii
        Set-Content -LiteralPath (Join-Path $root 'Files\uninstall.cmd') -Value "@echo off`r`nexit /b %1" -Encoding Ascii

        $projectId = 'psadt-it-' + [System.Guid]::NewGuid().ToString('N').Substring(0, 8)
        $config = [IntunePackageBuilder.Core.Versions.PackageVersionConfig]::CreateDefault($projectId, [IntunePackageBuilder.Core.Versions.InstallerType]::Exe)
        $config.Identity.SoftwareName = 'Integration Test App'
        $config.Identity.Manufacturer = 'Contoso'
        $config.Identity.TargetVersion = '1.0.0'
        $config.Source.InstallerRelativePath = 'setup.cmd'
        $config.Install.Arguments = $InstallerExitCode
        $config.Uninstall.ExecutablePath = (Join-Path $root 'Files\uninstall.cmd')
        $config.Uninstall.Arguments = '0'
        $config.Detection.Path = $DetectionPath
        $config.Detection.MinimumVersion = '1.0'
        $config.Deployment.Engine = [IntunePackageBuilder.Core.Versions.DeploymentEngine]::Psadt
        & $Configure $config $root

        $manifest = New-Object IntunePackageBuilder.Core.Sources.SourceManifest
        $manifest.CreatedUtc = [datetime]::UtcNow
        $file = New-Object IntunePackageBuilder.Core.Sources.SourceFileEntry
        $file.Path = 'setup.cmd'
        $file.Size = 1
        $file.Sha256 = ('a' * 64)
        $manifest.Files.Add($file)
        $snapshot = [IntunePackageBuilder.Core.Builds.BuildSnapshot]::Create($config, $manifest, '20261010-120000-abcd', $Language, [datetime]::UtcNow)
        $intune = Join-Path $script:work ([System.Guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $intune | Out-Null
        [void][IntunePackageBuilder.Generation.BuildArtifacts]::Write($snapshot, $null, $intune, $root)
        $script:logFolders.Add('C:\Windows\Logs\Intune\PackageDeploy\' + $projectId)
        return [pscustomobject]@{ Root = $root; ProjectId = $projectId }
    }

    function Invoke-ToolkitEntry {
        param([string]$Root, [string[]]$Arguments = @())
        $out = Join-Path $script:work ([System.Guid]::NewGuid().ToString('N') + '.out')
        $err = Join-Path $script:work ([System.Guid]::NewGuid().ToString('N') + '.err')
        $logDir = Join-Path $script:work ([System.Guid]::NewGuid().ToString('N'))
        $argumentList = @('-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', ('"{0}"' -f (Join-Path $Root 'Invoke-AppDeployToolkit.ps1')), '-DeployMode', 'Silent', '-LogDirectory', ('"{0}"' -f $logDir)) + $Arguments
        $process = Start-Process -FilePath (Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe') -ArgumentList $argumentList -NoNewWindow -Wait -PassThru -RedirectStandardOutput $out -RedirectStandardError $err
        $text = ([string](Get-Content -Raw -LiteralPath $out -ErrorAction SilentlyContinue)) + ([string](Get-Content -Raw -LiteralPath $err -ErrorAction SilentlyContinue))
        $log = Join-Path $logDir 'Deployment.log'
        return [pscustomobject]@{
            ExitCode = $process.ExitCode
            Output   = $text
            Log      = $(if (Test-Path -LiteralPath $log) { Get-Content -Raw -LiteralPath $log } else { '' })
            LogDir   = $logDir
        }
    }

    # Finds a parameter set that accepts all of the given parameters. Parameters the toolkit adds dynamically are mandatory only when
    # no session is open (Title, Subtitle); the entry script always runs inside a session, so they do not count as missing here.
    function Test-ParameterSet {
        param([string]$Command, [string[]]$Names)
        $dynamicWithoutSession = @('Title', 'Subtitle')
        $closest = $null
        foreach ($set in (Get-Command -Name $Command).ParameterSets) {
            $known = @($set.Parameters | ForEach-Object { $_.Name })
            $unknown = @($Names | Where-Object { $known -notcontains $_ })
            $missing = @($set.Parameters | Where-Object { $_.IsMandatory -and $Names -notcontains $_.Name -and $dynamicWithoutSession -notcontains $_.Name } | ForEach-Object { $_.Name })
            if ($unknown.Count -eq 0 -and $missing.Count -eq 0) { return [pscustomobject]@{ Ok = $true; Detail = $set.Name } }
            $score = $unknown.Count + $missing.Count
            if ($null -eq $closest -or $score -lt $closest.Score) {
                $closest = [pscustomobject]@{ Score = $score; Detail = ("closest set '{0}': unknown [{1}], mandatory but not given [{2}]" -f $set.Name, ($unknown -join ', '), ($missing -join ', ')) }
            }
        }
        return [pscustomobject]@{ Ok = $false; Detail = $closest.Detail }
    }
}

AfterAll {
    foreach ($folder in $script:logFolders) { Remove-Item -LiteralPath $folder -Recurse -Force -ErrorAction SilentlyContinue }
    Remove-Item -Recurse -Force $script:work -ErrorAction SilentlyContinue
}

Describe 'The entry script and the toolkit' -Skip:(-not $script:hasToolkit) {
    It 'offers the commands the entry script uses' {
        Import-Module -Name $script:moduleManifest -Force
        foreach ($name in 'Open-ADTSession', 'Close-ADTSession', 'Show-ADTInstallationWelcome', 'Show-ADTInstallationProgress', 'Write-ADTLogEntry', 'Initialize-ADTModule', 'Get-ADTConfig', 'Get-ADTStringTable') {
            Get-Command -Name $name -ErrorAction SilentlyContinue | Should -Not -BeNullOrEmpty -Because $name
        }
    }

    It 'resolves the parameter set of <Command> for: <Name>' -ForEach $script:welcomeCombinations {
        Import-Module -Name $script:moduleManifest -Force
        $result = Test-ParameterSet -Command $Command -Names $Names
        $result.Ok | Should -BeTrue -Because (($Names -join ', ') + ' - ' + $result.Detail)
    }

    It 'builds the commands only from the combinations tested above' {
        $tokens = $null; $errors = $null
        $ast = [System.Management.Automation.Language.Parser]::ParseFile((Join-Path $script:entryTemplate 'Invoke-AppDeployToolkit.ps1'), [ref]$tokens, [ref]$errors)
        $errors | Should -BeNullOrEmpty
        # Keys set one by one ($welcome['Key'] = ...) and keys of the table $prompt = @{ Key = ... }.
        $used = New-Object System.Collections.Generic.List[string]
        foreach ($node in $ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.IndexExpressionAst] -and ($n.Target.Extent.Text -eq '$welcome' -or $n.Target.Extent.Text -eq '$prompt') }, $true)) { $used.Add($node.Index.Value) }
        foreach ($assignment in $ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.AssignmentStatementAst] -and ($n.Left.Extent.Text -eq '$welcome' -or $n.Left.Extent.Text -eq '$prompt') }, $true)) {
            foreach ($table in $assignment.Right.FindAll({ param($n) $n -is [System.Management.Automation.Language.HashtableAst] }, $true)) {
                foreach ($pair in $table.KeyValuePairs) { $used.Add($pair.Item1.Value) }
            }
        }
        $tested = @($script:welcomeCombinations | ForEach-Object { $_.Names } | Sort-Object -Unique)
        # Timeout is a dynamic parameter of the prompt and not part of the static parameter sets.
        (@($used | Where-Object { $_ -ne 'Timeout' } | Sort-Object -Unique)) | Should -Be $tested
    }

    It 'passes only parameters that Open-ADTSession has' {
        Import-Module -Name $script:moduleManifest -Force
        $tokens = $null; $errors = $null
        $ast = [System.Management.Automation.Language.Parser]::ParseFile((Join-Path $script:entryTemplate 'Invoke-AppDeployToolkit.ps1'), [ref]$tokens, [ref]$errors)
        $assignment = $ast.FindAll({ param($node) $node -is [System.Management.Automation.Language.AssignmentStatementAst] -and $node.Left.Extent.Text -eq '$sessionParameters' }, $true) | Select-Object -First 1
        $assignment | Should -Not -BeNullOrEmpty
        $table = $assignment.Right.FindAll({ param($node) $node -is [System.Management.Automation.Language.HashtableAst] }, $true) | Select-Object -First 1
        $keys = @($table.KeyValuePairs | ForEach-Object { $_.Item1.Value })
        $keys.Count | Should -BeGreaterThan 5
        $known = (Get-Command -Name 'Open-ADTSession').Parameters.Keys
        @($keys | Where-Object { $known -notcontains $_ }) | Should -BeNullOrEmpty
    }
}

Describe 'The generated overlay is merged into the toolkit configuration' -Skip:(-not $script:hasToolkit) {
    It 'changes what the package asks for and keeps everything else' {
        $package = New-ToolkitPackage -Language 'de' -Configure {
            param($config, $root)
            $config.Deployment.Psadt.DialogStyle = [IntunePackageBuilder.Core.Versions.PsadtDialogStyle]::Classic
            $config.Deployment.Psadt.AccentColor = '#0078D4'
            $config.Deployment.Psadt.CompanyName = 'Fabrikam IT'
            $config.Deployment.Psadt.LogoFile = 'logo.png'
            $config.Deployment.Psadt.BalloonNotifications = $false
            $config.Interaction.DetailMessage = 'Bitte Dokumente sichern.'
            $config.Interaction.InstallMessage = 'Programm wird eingerichtet'
            New-Item -ItemType Directory -Path (Join-Path $root 'Assets') | Out-Null
            [System.IO.File]::WriteAllBytes((Join-Path $root 'Assets\logo.png'), [byte[]](0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0))
        }
        $probe = Join-Path $script:work 'probe.ps1'
        $resultFile = Join-Path $script:work 'probe-result.json'
        Set-Content -LiteralPath $probe -Encoding UTF8 -Value @"
`$ErrorActionPreference = 'Stop'
Import-Module -Name '$script:moduleManifest' -Force
Initialize-ADTModule -ScriptDirectory '$($package.Root)'
`$c = Get-ADTConfig
`$s = Get-ADTStringTable
[pscustomobject]@{
    Company = `$c.Toolkit.CompanyName; LogPath = `$c.Toolkit.LogPath; Style = `$c.UI.DialogStyle; Accent = `$c.UI.FluentAccentColor
    Exit = `$c.UI.DefaultExitCode; Defer = `$c.UI.DeferExitCode; Timeout = `$c.UI.DefaultTimeout; Language = `$c.UI.LanguageOverride
    Balloon = `$c.UI.BalloonNotifications; Logo = `$c.Assets.Logo; Banner = `$c.Assets.Banner; MsiInstallParams = `$c.MSI.InstallParams
    Dialog = `$s.CloseAppsPrompt.Fluent.DialogMessage.Install; Classic = `$s.CloseAppsPrompt.Classic.CloseAppsMessage.Uninstall
    Custom = `$s.CloseAppsPrompt.CustomMessage; Progress = `$s.ProgressPrompt.Message.Install; ProgressUninstall = `$s.ProgressPrompt.Message.Uninstall
} | ConvertTo-Json | Set-Content -LiteralPath '$resultFile' -Encoding UTF8
"@
        & (Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe') -NoProfile -NonInteractive -ExecutionPolicy Bypass -File $probe | Out-Null
        $LASTEXITCODE | Should -Be 0
        $result = [System.IO.File]::ReadAllText($resultFile, [System.Text.Encoding]::UTF8) | ConvertFrom-Json
        $result.Company | Should -Be 'Fabrikam IT'
        $result.LogPath | Should -Be ('C:\Windows\Logs\Intune\PackageDeploy\' + $package.ProjectId)
        $result.Style | Should -Be 'Classic'
        [int]$result.Accent | Should -Be ([int]0xFF0078D4)
        $result.Exit | Should -Be 1618
        $result.Defer | Should -Be 1618
        $result.Timeout | Should -Be 1200
        $result.Language | Should -Be 'de'
        $result.Balloon | Should -BeFalse
        $result.Logo | Should -Be (Join-Path $package.Root 'Assets\logo.png')
        $result.Banner | Should -Not -BeNullOrEmpty -Because 'the toolkit keeps its own banner when the package has none'
        $result.MsiInstallParams | Should -Not -BeNullOrEmpty -Because 'values the overlay does not mention keep their defaults'
        $result.Dialog | Should -Be 'Bitte speichern Sie Ihre Arbeit und schließen Sie die folgenden Programme. Die Installation fährt automatisch fort, sobald sie geschlossen sind.'
        $result.Classic | Should -BeLike 'Bitte speichern Sie Ihre Arbeit*Das Entfernen*'
        $result.Custom | Should -Be 'Bitte Dokumente sichern.'
        $result.Progress | Should -Be 'Programm wird eingerichtet'
        $result.ProgressUninstall | Should -Not -BeNullOrEmpty
    }
}

Describe 'The entry script runs a deployment through the toolkit (Silent mode)' -Skip:(-not $script:hasToolkit) {
    It 'installs, verifies the target state and logs in both places: installer exit code <Code> gives <Expected>' -ForEach @(
        @{ Code = '0'; Expected = 0 }, @{ Code = '3010'; Expected = 3010 }, @{ Code = '1618'; Expected = 1618 }, @{ Code = '5'; Expected = 5 }, @{ Code = '1641'; Expected = 1641 }
    ) {
        $package = New-ToolkitPackage -InstallerExitCode $Code
        $started = Get-Date
        $result = Invoke-ToolkitEntry -Root $package.Root
        $result.ExitCode | Should -Be $Expected -Because $result.Output
        $result.Log | Should -Match 'Install of'
        $toolkitLog = Get-ChildItem -LiteralPath ('C:\Windows\Logs\Intune\PackageDeploy\' + $package.ProjectId) -Filter '*.log' -ErrorAction SilentlyContinue | Where-Object { $_.LastWriteTime -ge $started }
        @($toolkitLog).Count | Should -BeGreaterThan 0 -Because 'the toolkit writes its own log to the folder of the package'
        if ($Expected -eq 0 -or $Expected -eq 3010) { $result.Log | Should -Match 'target state was verified' }
    }

    It 'does not accept success when the target state is not reached' {
        $package = New-ToolkitPackage -DetectionPath (Join-Path $script:work 'never-installed.exe')
        $result = Invoke-ToolkitEntry -Root $package.Root
        $result.ExitCode | Should -Be 60001 -Because $result.Output
    }

    It 'uninstalls with -DeploymentType Uninstall' {
        $package = New-ToolkitPackage -DetectionPath (Join-Path $script:work 'gone.exe')
        $result = Invoke-ToolkitEntry -Root $package.Root -Arguments @('-DeploymentType', 'Uninstall')
        $result.ExitCode | Should -Be 0 -Because $result.Output
        $result.Log | Should -Match 'Uninstall of'
    }

    It 'ends with the retry result and ends nothing by force when a program to close runs and no dialog may be shown' {
        $package = New-ToolkitPackage -Configure { param($config, $root) $config.Interaction.ProcessesToClose.Add('ping.exe') }
        $ping = Start-Process -FilePath (Join-Path $env:SystemRoot 'System32\PING.EXE') -ArgumentList @('-n', '120', '127.0.0.1') -PassThru -WindowStyle Hidden
        try {
            $result = Invoke-ToolkitEntry -Root $package.Root
            $result.ExitCode | Should -Be 1618 -Because $result.Output
            $result.Log | Should -Not -Match 'Starting '
            $ping.HasExited | Should -BeFalse -Because 'the toolkit must not end the program'
        }
        finally {
            if (-not $ping.HasExited) { $ping.Kill() }
        }
    }

    It 'reports a missing toolkit folder and a missing configuration with their own codes' {
        $package = New-ToolkitPackage
        Remove-Item -LiteralPath (Join-Path $package.Root 'PSAppDeployToolkit') -Recurse -Force
        (Invoke-ToolkitEntry -Root $package.Root).ExitCode | Should -Be 60008

        $other = New-ToolkitPackage
        Remove-Item -LiteralPath (Join-Path $other.Root 'Deployment.config.json')
        (Invoke-ToolkitEntry -Root $other.Root).ExitCode | Should -Be 60002
    }

    It 'refuses a configuration that selects the built-in runtime' {
        $package = New-ToolkitPackage
        $path = Join-Path $package.Root 'Deployment.config.json'
        $json = [System.IO.File]::ReadAllText($path, [System.Text.Encoding]::UTF8).Replace('"engine": "Psadt"', '"engine": "Native"')
        [System.IO.File]::WriteAllText($path, $json, (New-Object System.Text.UTF8Encoding($true)))
        (Invoke-ToolkitEntry -Root $package.Root).ExitCode | Should -Be 60002
    }
}
