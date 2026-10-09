# Repository-level checks for own PowerShell files (SPEC section 4):
# UTF-8 with BOM and no PowerShell 7 only syntax. Runs under Windows PowerShell 5.1 and Pester 5.

BeforeAll {
    function Test-Utf8Bom {
        param([string]$Path)
        $stream = [System.IO.File]::OpenRead($Path)
        try {
            $buffer = New-Object byte[] 3
            $read = $stream.Read($buffer, 0, 3)
            return ($read -eq 3 -and $buffer[0] -eq 0xEF -and $buffer[1] -eq 0xBB -and $buffer[2] -eq 0xBF)
        }
        finally {
            $stream.Dispose()
        }
    }

    function Find-Ps7OnlySyntax {
        param([string]$Text)
        $patterns = @(
            @{ Name = 'null-coalescing operator'; Regex = '\?\?' },
            @{ Name = 'pipeline chain operator'; Regex = '&&|\|\|' },
            @{ Name = 'ForEach-Object -Parallel'; Regex = 'ForEach-Object\s+-Parallel' },
            @{ Name = 'ternary operator'; Regex = '\)\s*\?\s*[^\s]' }
        )
        foreach ($pattern in $patterns) {
            if ($Text -match $pattern.Regex) { $pattern.Name }
        }
    }
}

Describe 'BOM detection helper' {
    BeforeAll {
        $script:tempDir = Join-Path ([System.IO.Path]::GetTempPath()) ([System.Guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $script:tempDir | Out-Null
    }

    AfterAll {
        Remove-Item -Recurse -Force $script:tempDir -ErrorAction SilentlyContinue
    }

    It 'accepts a file that starts with a UTF-8 BOM' {
        $file = Join-Path $script:tempDir 'with-bom.ps1'
        [System.IO.File]::WriteAllBytes($file, [byte[]](0xEF, 0xBB, 0xBF, 0x41))
        Test-Utf8Bom -Path $file | Should -BeTrue
    }

    It 'rejects a file without a BOM' {
        $file = Join-Path $script:tempDir 'no-bom.ps1'
        [System.IO.File]::WriteAllBytes($file, [byte[]](0x41, 0x42, 0x43, 0x44))
        Test-Utf8Bom -Path $file | Should -BeFalse
    }

    It 'rejects a file that is shorter than the BOM' {
        $file = Join-Path $script:tempDir 'short.ps1'
        [System.IO.File]::WriteAllBytes($file, [byte[]](0xEF, 0xBB))
        Test-Utf8Bom -Path $file | Should -BeFalse
    }
}

Describe 'PowerShell 7 syntax detection helper' {
    It 'reports <Name>' -ForEach @(
        @{ Name = 'null-coalescing operator'; Code = '$a = $b ?? 1' },
        @{ Name = 'pipeline chain operator'; Code = 'Get-Item x && Get-Item y' },
        @{ Name = 'ForEach-Object -Parallel'; Code = '1..3 | ForEach-Object -Parallel { $_ }' },
        @{ Name = 'ternary operator'; Code = '$x = ($a -gt 1) ? 2 : 3' }
    ) {
        @(Find-Ps7OnlySyntax -Text $Code) | Should -Contain $Name
    }

    It 'accepts plain Windows PowerShell 5.1 code' {
        @(Find-Ps7OnlySyntax -Text 'if ($a -eq 1) { Write-Output "ok" }') | Should -BeNullOrEmpty
    }
}

Describe 'Own PowerShell files' {
    BeforeAll {
        $repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path

        function Get-OwnScript {
            param([string[]]$Directory, [string[]]$Include)
            foreach ($dir in $Directory) {
                $path = Join-Path $repoRoot $dir
                if (Test-Path $path) {
                    Get-ChildItem -Path $path -Recurse -File -Include $Include |
                        Where-Object { $_.FullName -notmatch '\\(vendor|bin|obj|node_modules)\\' }
                }
            }
        }
    }

    It 'all start with a UTF-8 BOM' {
        $offenders = @(Get-OwnScript -Directory 'deploy', 'src', 'tests', 'tools' -Include '*.ps1', '*.psm1', '*.psd1' |
            Where-Object { -not (Test-Utf8Bom -Path $_.FullName) } |
            ForEach-Object { $_.FullName.Substring($repoRoot.Length + 1) })
        $offenders | Should -BeNullOrEmpty -Because "own PowerShell files must be UTF-8 with BOM: $($offenders -join ', ')"
    }

    It 'use no PowerShell 7 only syntax' {
        $offenders = @(Get-OwnScript -Directory 'deploy', 'src', 'tools' -Include '*.ps1', '*.psm1' |
            ForEach-Object {
                $found = @(Find-Ps7OnlySyntax -Text (Get-Content -Raw -Path $_.FullName))
                if ($found.Count -gt 0) { '{0}: {1}' -f $_.FullName.Substring($repoRoot.Length + 1), ($found -join ', ') }
            })
        $offenders | Should -BeNullOrEmpty -Because "PowerShell 7 only syntax found: $($offenders -join '; ')"
    }
}
