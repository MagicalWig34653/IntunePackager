# Distribution tooling (SPEC sections 4 and 13, acceptance check A18).
# Builds distributions from stand-in files and shows that the check accepts the right content and rejects everything else.
# Runs under Windows PowerShell 5.1 and Pester 5.

BeforeAll {
    $script:repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
    Import-Module (Join-Path $script:repoRoot 'tools\Distribution.psm1') -Force

    $script:work = Join-Path ([System.IO.Path]::GetTempPath()) ('ipb-dist-test-' + [System.Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $script:work | Out-Null

    function New-TextFile {
        param([string]$Path, [string]$Text)
        New-Item -ItemType Directory -Path (Split-Path -Parent $Path) -Force | Out-Null
        [System.IO.File]::WriteAllText($Path, $Text)
    }

    # A build output as the compiler leaves it: the files to ship plus debug files, XML documentation and test frameworks.
    $script:buildOutput = Join-Path $script:work 'build'
    foreach ($name in 'IntunePackageBuilder.exe', 'IntunePackageBuilder.exe.config', 'IntunePackageBuilder.Core.dll',
        'IntunePackageBuilder.Analysis.dll', 'IntunePackageBuilder.Build.dll', 'IntunePackageBuilder.Generation.dll',
        'Newtonsoft.Json.dll', 'de\IntunePackageBuilder.resources.dll', 'de\IntunePackageBuilder.Generation.resources.dll',
        'template\Install.cmd', 'template\Deploy-Wrapper.ps1', 'template\DeployCore.psm1',
        'template\Messages.de.psd1', 'template\Messages.en.psd1',
        'IntunePackageBuilder.pdb', 'IntunePackageBuilder.Core.xml', 'xunit.core.dll') {
        New-TextFile -Path (Join-Path $script:buildOutput $name) -Text ('stand-in for ' + $name)
    }

    function New-FakeRepository {
        param([string]$Name, [switch]$WithoutNewtonsoftHash)
        $root = Join-Path $script:work $Name
        $hash = Get-Sha256Text -Path (Join-Path $script:buildOutput 'Newtonsoft.Json.dll')
        $third = if ($WithoutNewtonsoftHash) { 'Newtonsoft.Json: no checksum here' } else { "Newtonsoft.Json: $hash" }
        New-TextFile -Path (Join-Path $root 'LICENSE') -Text 'license'
        New-TextFile -Path (Join-Path $root 'THIRD-PARTY.md') -Text $third
        New-TextFile -Path (Join-Path $root 'README.md') -Text 'readme'
        New-TextFile -Path (Join-Path $root 'README.en.md') -Text 'readme'
        New-TextFile -Path (Join-Path $root 'docs\BEDIENUNG.md') -Text 'manual'
        $root
    }

    function New-FakeDistribution {
        param([string]$Name, [string]$Repository)
        $destination = Join-Path $script:work $Name
        New-DistributionFolder -BuildOutput $script:buildOutput -RepositoryRoot $Repository -Destination $destination -Version '0.0.1'
        $destination
    }

    $script:goodRepository = New-FakeRepository -Name 'repo-good'
    $script:powershell = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'

    function Invoke-ChildScript {
        # Runs a script in a child Windows PowerShell 5.1 process; its error output is captured and never raised here.
        param([string]$Script, [string[]]$Argument)
        $quoted = @($Argument | ForEach-Object { '"' + $_ + '"' })
        $info = New-Object System.Diagnostics.ProcessStartInfo
        $info.FileName = $script:powershell
        $info.Arguments = '-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' + $Script + '" ' + ($quoted -join ' ')
        $info.UseShellExecute = $false
        $info.RedirectStandardOutput = $true
        $info.RedirectStandardError = $true
        $process = [System.Diagnostics.Process]::Start($info)
        $errorTask = $process.StandardError.ReadToEndAsync()
        $output = $process.StandardOutput.ReadToEnd()
        $process.WaitForExit()
        [pscustomobject]@{ ExitCode = $process.ExitCode; Output = $output; Error = $errorTask.Result }
    }
}

AfterAll {
    Remove-Item -Recurse -Force $script:work -ErrorAction SilentlyContinue
}

Describe 'A18 - distribution content' {
    It 'passes for a distribution of allow-listed files with correct checksums' {
        $folder = New-FakeDistribution -Name 'ok' -Repository $script:goodRepository
        @(Get-DistributionProblem -Folder $folder) | Should -BeNullOrEmpty
    }

    It 'leaves debug files, XML documentation and test frameworks behind' {
        $folder = New-FakeDistribution -Name 'clean' -Repository $script:goodRepository
        foreach ($name in 'IntunePackageBuilder.pdb', 'IntunePackageBuilder.Core.xml', 'xunit.core.dll') {
            Test-Path -LiteralPath (Join-Path $folder $name) | Should -BeFalse -Because "$name must not be shipped"
        }
        Test-Path -LiteralPath (Join-Path $folder 'template\Install.cmd') | Should -BeTrue
        Test-Path -LiteralPath (Join-Path $folder 'SHA256SUMS.txt') | Should -BeTrue
    }

    It 'declares the program as unsigned when it is not signed' {
        $folder = New-FakeDistribution -Name 'unsigned' -Repository $script:goodRepository
        $manifest = Get-Content -Raw -Path (Join-Path $folder 'distribution-manifest.json') | ConvertFrom-Json
        $manifest.signatureStatus | Should -Be 'unsigned'
        $manifest.version | Should -Be '0.0.1'
    }

    It 'rejects <Name>' -ForEach @(
        @{ Name = 'a vendor installer'; Relative = 'vendor\setup.msi'; Message = 'Forbidden file' },
        @{ Name = 'a built package'; Relative = 'out\app.intunewin'; Message = 'Forbidden file' },
        @{ Name = 'the Content Prep Tool'; Relative = 'IntuneWinAppUtil.exe'; Message = 'Forbidden file' },
        @{ Name = 'a project file'; Relative = 'projects\a\project.json'; Message = 'Forbidden file' },
        @{ Name = 'a version configuration'; Relative = 'configuration.json'; Message = 'Forbidden file' },
        @{ Name = 'a settings file'; Relative = 'settings.json'; Message = 'Forbidden file' },
        @{ Name = 'a debug file'; Relative = 'IntunePackageBuilder.pdb'; Message = 'Forbidden file' },
        @{ Name = 'a test assembly'; Relative = 'IntunePackageBuilder.Core.Tests.dll'; Message = 'Forbidden file' },
        @{ Name = 'a UI test library'; Relative = 'FlaUI.Core.dll'; Message = 'Forbidden file' },
        @{ Name = 'a file that is on no list'; Relative = 'notes.txt'; Message = 'not on the allow list' }
    ) {
        $folder = New-FakeDistribution -Name ('bad-' + [System.Guid]::NewGuid().ToString('N')) -Repository $script:goodRepository
        New-TextFile -Path (Join-Path $folder $Relative) -Text 'unwanted'
        $problems = (@(Get-DistributionProblem -Folder $folder)) -join "`n"
        $problems | Should -Match $Message
        $problems | Should -Match ([regex]::Escape(($Relative -replace '\\', '/')))
    }

    It 'rejects a distribution that lacks a required file' {
        $folder = New-FakeDistribution -Name 'incomplete' -Repository $script:goodRepository
        Remove-Item -LiteralPath (Join-Path $folder 'template\Install.cmd')
        (@(Get-DistributionProblem -Folder $folder)) -join "`n" | Should -Match 'Required file is missing'
    }

    It 'rejects a file that changed after the checksums were written' {
        $folder = New-FakeDistribution -Name 'changed' -Repository $script:goodRepository
        [System.IO.File]::AppendAllText((Join-Path $folder 'de\IntunePackageBuilder.resources.dll'), 'tampered')
        (@(Get-DistributionProblem -Folder $folder)) -join "`n" | Should -Match 'Checksum mismatch'
    }

    It 'rejects an allowed file that has no checksum' {
        $folder = New-FakeDistribution -Name 'unlisted' -Repository $script:goodRepository
        New-TextFile -Path (Join-Path $folder 'docs\DATENFORMAT.md') -Text 'added later'
        (@(Get-DistributionProblem -Folder $folder)) -join "`n" | Should -Match 'File without checksum'
    }

    It 'rejects a distribution without checksum file' {
        $folder = New-FakeDistribution -Name 'nosums' -Repository $script:goodRepository
        Remove-Item -LiteralPath (Join-Path $folder 'SHA256SUMS.txt')
        (@(Get-DistributionProblem -Folder $folder)) -join "`n" | Should -Match 'SHA256SUMS.txt is missing'
    }

    It 'rejects a shipped library whose checksum THIRD-PARTY.md does not document' {
        $repository = New-FakeRepository -Name 'repo-nohash' -WithoutNewtonsoftHash
        $folder = New-FakeDistribution -Name 'nohash' -Repository $repository
        (@(Get-DistributionProblem -Folder $folder)) -join "`n" | Should -Match 'does not document the SHA-256'
    }

    It 'rejects a manifest that claims a signature the program does not have' {
        $folder = New-FakeDistribution -Name 'claims' -Repository $script:goodRepository
        $path = Join-Path $folder 'distribution-manifest.json'
        [System.IO.File]::WriteAllText($path, ([System.IO.File]::ReadAllText($path)).Replace('"unsigned"', '"signed"'))
        (@(Get-DistributionProblem -Folder $folder)) -join "`n" | Should -Match 'is not signed'
    }
}

Describe 'A18 - the scripts' {
    BeforeAll {
        $script:newScript = Join-Path $script:repoRoot 'tools\New-Distribution.ps1'
        $script:testScript = Join-Path $script:repoRoot 'tools\Test-Distribution.ps1'
        function Invoke-NewDistribution {
            param([string]$Output)
            Invoke-ChildScript -Script $script:newScript -Argument @(
                '-BuildOutput', $script:buildOutput, '-OutputDirectory', $Output, '-RepositoryRoot', $script:goodRepository, '-Version', '0.0.1')
        }
    }

    It 'builds a ZIP and the check accepts it' {
        $output = Join-Path $script:work 'zip-ok'
        $result = Invoke-NewDistribution -Output $output
        $result.ExitCode | Should -Be 0 -Because ($result.Output + $result.Error)
        $zip = Join-Path $output 'IntunePackageBuilder-0.0.1.zip'
        Test-Path -LiteralPath $zip | Should -BeTrue

        $check = Invoke-ChildScript -Script $script:testScript -Argument @('-Path', $zip)
        $check.ExitCode | Should -Be 0 -Because ($check.Output + $check.Error)
    }

    It 'fails the check for a ZIP that contains a vendor installer' {
        $output = Join-Path $script:work 'zip-bad'
        (Invoke-NewDistribution -Output $output).ExitCode | Should -Be 0
        $zip = Join-Path $output 'IntunePackageBuilder-0.0.1.zip'
        Add-Type -AssemblyName System.IO.Compression
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $archive = [System.IO.Compression.ZipFile]::Open($zip, [System.IO.Compression.ZipArchiveMode]::Update)
        try {
            $entry = $archive.CreateEntry('IntunePackageBuilder-0.0.1/vendor/setup.msi')
            $writer = New-Object System.IO.StreamWriter($entry.Open())
            $writer.Write('not allowed')
            $writer.Dispose()
        }
        finally {
            $archive.Dispose()
        }

        $check = Invoke-ChildScript -Script $script:testScript -Argument @('-Path', $zip)
        $check.ExitCode | Should -Be 1
        ($check.Output + $check.Error) | Should -Match 'Forbidden file'
    }

    It 'refuses to overwrite an existing ZIP' {
        $output = Join-Path $script:work 'zip-twice'
        (Invoke-NewDistribution -Output $output).ExitCode | Should -Be 0
        $second = Invoke-NewDistribution -Output $output
        $second.ExitCode | Should -Not -Be 0
        ($second.Output + $second.Error) | Should -Match 'already exists'
    }
}
