# PSAppDeployToolkit pin tooling (docs/PLANUNG.md section 8, number 12).
# Shows that the update helpers write a stable pin file and keep THIRD-PARTY.md in step. No network access.
# Runs under Windows PowerShell 5.1 and Pester 5.

BeforeAll {
    $script:repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
    Import-Module (Join-Path $script:repoRoot 'tools\Psadt.psm1') -Force
    $script:pinText = [System.IO.File]::ReadAllText((Join-Path $script:repoRoot 'deploy\psadt\psadt.json'))
    $script:thirdParty = [System.IO.File]::ReadAllText((Join-Path $script:repoRoot 'THIRD-PARTY.md'))
    $script:hash = 'a' * 64
    $script:commit = 'b' * 40
}

Describe 'ConvertTo-PsadtVersion' {
    It 'strips a leading v' {
        ConvertTo-PsadtVersion -Tag 'v4.2.0' | Should -Be '4.2.0'
        ConvertTo-PsadtVersion -Tag '4.1.8' | Should -Be '4.1.8'
    }

    It 'rejects tags that are not x.y.z' {
        { ConvertTo-PsadtVersion -Tag '4.2.0-beta1' } | Should -Throw
        { ConvertTo-PsadtVersion -Tag 'latest' } | Should -Throw
    }
}

Describe 'Get-PsadtBundledLibrary' {
    It 'lists only files directly in lib, without symbols, configs and localized folders' {
        $names = @(
            'PSAppDeployToolkit/lib/PSADT.dll', 'PSAppDeployToolkit/lib/PSADT.pdb', 'PSAppDeployToolkit/lib/PSADT.ClientServer.Client.exe.config',
            'PSAppDeployToolkit/lib/de-DE/iNKORE.UI.WPF.Modern.resources.dll', 'PSAppDeployToolkit/lib/Newtonsoft.Json.dll',
            'PSAppDeployToolkit/Config/config.psd1')
        @(Get-PsadtBundledLibrary -EntryName $names) | Should -Be @('Newtonsoft.Json.dll', 'PSADT.dll')
    }
}

Describe 'The recorded pin' {
    It 'is valid JSON with at least one version, newest first, each with a 64-digit SHA-256 and a 40-digit commit' {
        $pin = $script:pinText | ConvertFrom-Json
        @($pin.versions).Count | Should -BeGreaterThan 0
        foreach ($v in @($pin.versions)) {
            $v.sha256 | Should -Match '^[0-9a-f]{64}$'
            $v.commit | Should -Match '^[0-9a-f]{40}$'
            @($v.bundledLibraries).Count | Should -BeGreaterThan 0
        }
    }

    It 'matches the PSAppDeployToolkit row of THIRD-PARTY.md' {
        $pin = $script:pinText | ConvertFrom-Json
        $newest = @($pin.versions)[0]
        $row = @($script:thirdParty -split "`n" | Where-Object { $_ -like '| PSAppDeployToolkit (*' })
        $row.Count | Should -Be 1
        $row[0] | Should -BeLike ('*| ' + $newest.version + ' (Commit `' + $newest.commit + '`)*')
        $row[0] | Should -BeLike ('*`' + $newest.sha256 + '`*')
    }

    It 'is written back unchanged when the newest version is added again' {
        $pin = $script:pinText | ConvertFrom-Json
        $newest = @($pin.versions)[0]
        $entry = [ordered]@{ version = $newest.version; asset = $newest.asset; sha256 = $newest.sha256; commit = $newest.commit; bundledLibraries = @($newest.bundledLibraries) }
        $again = Add-PsadtPinVersion -PinJson $script:pinText -Entry $entry
        $again | Should -Be ($script:pinText -replace "`r`n", "`n")
    }
}

Describe 'Add-PsadtPinVersion' {
    It 'puts the new version first and keeps the earlier ones' {
        $entry = [ordered]@{ version = '9.9.9'; asset = 'PSAppDeployToolkit_Template_v4.zip'; sha256 = $script:hash; commit = $script:commit; bundledLibraries = @('PSADT.dll') }
        $json = Add-PsadtPinVersion -PinJson $script:pinText -Entry $entry
        $pin = $json | ConvertFrom-Json
        @($pin.versions).Count | Should -Be 2
        @($pin.versions)[0].version | Should -Be '9.9.9'
        @($pin.versions)[0].sha256 | Should -Be $script:hash
        @($pin.versions)[1].version | Should -Be '4.1.8'
        $json | Should -Not -Match "`r"
        $json.EndsWith("`n") | Should -BeTrue
    }
}

Describe 'Update-PsadtThirdPartyRow' {
    It 'changes version, commit and checksum of the toolkit row and nothing else' {
        $updated = Update-PsadtThirdPartyRow -Text $script:thirdParty -Version '9.9.9' -Commit $script:commit -Sha256 $script:hash
        $row = @($updated -split "`n" | Where-Object { $_ -like '| PSAppDeployToolkit (*' })
        $row[0] | Should -BeLike ('*| 9.9.9 (Commit `' + $script:commit + '`)*')
        $row[0] | Should -BeLike ('*`' + $script:hash + '` (ZIP der Veröffentlichung)*')
        $before = @($script:thirdParty -split "`n" | Where-Object { $_ -notlike '| PSAppDeployToolkit (*' })
        $after = @($updated -split "`n" | Where-Object { $_ -notlike '| PSAppDeployToolkit (*' })
        $after | Should -Be $before
    }

    It 'refuses a file without the row' {
        { Update-PsadtThirdPartyRow -Text 'nothing here' -Version '1.2.3' -Commit $script:commit -Sha256 $script:hash } | Should -Throw
    }
}

Describe 'New-PsadtPullRequestBody' {
    It 'flags new libraries and a changed license' {
        $body = New-PsadtPullRequestBody -Version '9.9.9' -Tag '9.9.9' -Sha256 $script:hash -Commit $script:commit -NewLibrary @('Some.dll') -LicenseChanged $true
        $body | Should -BeLike '*License review needed*COPYING.Lesser*'
        $body | Should -BeLike '*new bundled libraries*Some.dll*'
    }

    It 'says plainly when nothing changed in the licenses and that nothing was tested on a device' {
        $body = New-PsadtPullRequestBody -Version '9.9.9' -Tag '9.9.9' -Sha256 $script:hash -Commit $script:commit -NewLibrary @() -LicenseChanged $false
        $body | Should -BeLike '*No new bundled libraries*'
        $body | Should -BeLike '*Not verified*device*'
    }
}
