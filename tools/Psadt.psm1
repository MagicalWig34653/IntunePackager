# Helpers for tools/Update-PsadtPin.ps1; kept free of network and file access so Pester can test them.

function ConvertTo-PsadtVersion {
    param([Parameter(Mandatory = $true)][string]$Tag)
    $version = $Tag.TrimStart('v', 'V')
    if ($version -notmatch '^\d+\.\d+\.\d+$') { throw "Unexpected PSAppDeployToolkit tag '$Tag' (expected x.y.z)." }
    return $version
}

# File names directly inside PSAppDeployToolkit/lib/ (not the localized resource folders, not symbols).
function Get-PsadtBundledLibrary {
    param([Parameter(Mandatory = $true)][string[]]$EntryName)
    $names = New-Object System.Collections.Generic.SortedSet[string] ([System.StringComparer]::Ordinal)
    foreach ($entry in $EntryName) {
        if ($entry -match '^PSAppDeployToolkit/lib/([^/]+)$' -and $Matches[1] -notmatch '\.pdb$' -and $Matches[1] -notmatch '\.config$') {
            [void]$names.Add($Matches[1])
        }
    }
    return @($names)
}

# Puts a new version first. Returns the whole JSON text (LF line ends, two-space indent, trailing newline).
# The text is written by hand because ConvertTo-Json formats differently in Windows PowerShell 5.1 and 7.
function Add-PsadtPinVersion {
    param(
        [Parameter(Mandatory = $true)][string]$PinJson,
        [Parameter(Mandatory = $true)]$Entry
    )
    $pin = $PinJson | ConvertFrom-Json
    $versions = New-Object System.Collections.Generic.List[object]
    $versions.Add([pscustomobject]$Entry)
    foreach ($existing in @($pin.versions)) {
        if ($existing.version -ne $Entry.version) { $versions.Add($existing) }
    }

    $blocks = New-Object System.Collections.Generic.List[string]
    foreach ($v in $versions) {
        $libraries = @($v.bundledLibraries | ForEach-Object { '        ' + (ConvertTo-JsonString $_) }) -join ",`n"
        $blocks.Add(@(
                '    {',
                ('      "version": ' + (ConvertTo-JsonString $v.version) + ','),
                ('      "asset": ' + (ConvertTo-JsonString $v.asset) + ','),
                ('      "sha256": ' + (ConvertTo-JsonString $v.sha256) + ','),
                ('      "commit": ' + (ConvertTo-JsonString $v.commit) + ','),
                '      "bundledLibraries": [',
                $libraries,
                '      ]',
                '    }') -join "`n")
    }

    return (@(
            '{',
            ('  "schemaVersion": ' + [int]$pin.schemaVersion + ','),
            ('  "source": ' + (ConvertTo-JsonString $pin.source) + ','),
            ('  "license": ' + (ConvertTo-JsonString $pin.license) + ','),
            ('  "licenseSha256": ' + (ConvertTo-JsonString $pin.licenseSha256) + ','),
            '  "versions": [',
            ($blocks -join ",`n"),
            '  ]',
            '}') -join "`n") + "`n"
}

function ConvertTo-JsonString {
    param([Parameter(Mandatory = $true)][string]$Value)
    $builder = New-Object System.Text.StringBuilder
    [void]$builder.Append('"')
    foreach ($c in $Value.ToCharArray()) {
        switch ($c) {
            '"' { [void]$builder.Append('\"') }
            '\' { [void]$builder.Append('\\') }
            default {
                if ([int]$c -lt 32) { [void]$builder.Append(('\u{0:x4}' -f [int]$c)) } else { [void]$builder.Append($c) }
            }
        }
    }
    [void]$builder.Append('"')
    return $builder.ToString()
}

# Rewrites version, commit and ZIP checksum in the one THIRD-PARTY.md row of the toolkit; keeps the line ends.
function Update-PsadtThirdPartyRow {
    param(
        [Parameter(Mandatory = $true)][string]$Text,
        [Parameter(Mandatory = $true)][string]$Version,
        [Parameter(Mandatory = $true)][string]$Commit,
        [Parameter(Mandatory = $true)][string]$Sha256
    )
    $pattern = '(?m)^(\| PSAppDeployToolkit \(`[^`]+`\) \| )\d+\.\d+\.\d+( \(Commit `)[0-9a-f]{40}(`\) \|.*?\| )`[0-9a-f]{64}`( \(ZIP der Veröffentlichung\))'
    if (-not [regex]::IsMatch($Text, $pattern)) { throw 'THIRD-PARTY.md has no PSAppDeployToolkit row in the expected form.' }
    $regex = New-Object System.Text.RegularExpressions.Regex($pattern)
    return $regex.Replace($Text, {
            param($m)
            $m.Groups[1].Value + $Version + $m.Groups[2].Value + $Commit + $m.Groups[3].Value + '`' + $Sha256 + '`' + $m.Groups[4].Value
        }, 1)
}

function New-PsadtPullRequestBody {
    param(
        [Parameter(Mandatory = $true)][string]$Version,
        [Parameter(Mandatory = $true)][string]$Tag,
        [Parameter(Mandatory = $true)][string]$Sha256,
        [Parameter(Mandatory = $true)][string]$Commit,
        [string[]]$NewLibrary,
        [bool]$LicenseChanged
    )
    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add("Automatic update of the pinned PSAppDeployToolkit to $Version (release $Tag).")
    $lines.Add('')
    $lines.Add("- Commit: ``$Commit``")
    $lines.Add("- SHA-256 of ``PSAppDeployToolkit_Template_v4.zip``: ``$Sha256`` (matches the digest GitHub reports)")
    $lines.Add('')
    $lines.Add('**Verified by this workflow:** download, checksum, license file present. **Not verified:** that the new version works on devices (Windows 11, Windows Server 2019 and newer); nothing is tested on a device in this PR.')
    $lines.Add('')
    if ($LicenseChanged) { $lines.Add('**License review needed:** `COPYING.Lesser` differs from the recorded one.') }
    if (@($NewLibrary).Count -gt 0) { $lines.Add('**License review needed:** new bundled libraries: ' + ((@($NewLibrary) | ForEach-Object { "``$_``" }) -join ', ')) }
    if (-not $LicenseChanged -and @($NewLibrary).Count -eq 0) { $lines.Add('No new bundled libraries and an unchanged license file.') }
    $lines.Add('')
    $lines.Add('Read the release notes, check the generated guide and packages against the new version, then merge once CI is green. This workflow never merges.')
    return ($lines -join "`n") + "`n"
}

Export-ModuleMember -Function ConvertTo-PsadtVersion, Get-PsadtBundledLibrary, Add-PsadtPinVersion, ConvertTo-JsonString, Update-PsadtThirdPartyRow, New-PsadtPullRequestBody
