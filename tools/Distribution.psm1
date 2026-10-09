# Builds and verifies the portable distribution of the program (SPEC sections 4 and 13, acceptance check A18).
# Windows PowerShell 5.1 compatible. Only files on the allow list are shipped; the rest is a problem, not a warning.

Set-StrictMode -Version 2.0

# Files that may be part of the distribution (relative paths with forward slashes, matched case-insensitively).
$script:AllowedFile = @(
    '^IntunePackageBuilder\.exe$',
    '^IntunePackageBuilder\.exe\.config$',
    '^IntunePackageBuilder\.(Core|Analysis|Build|Generation)\.dll$',
    '^Newtonsoft\.Json\.dll$',
    '^de/IntunePackageBuilder\.resources\.dll$',
    '^de/IntunePackageBuilder\.Generation\.resources\.dll$',
    '^template/(Install\.cmd|Deploy-Wrapper\.ps1|DeployCore\.psm1|Messages\.(de|en)\.psd1)$',
    '^(LICENSE|THIRD-PARTY\.md|README\.md|README\.en\.md)$',
    '^docs/(BEDIENUNG|DATENFORMAT|AENDERUNGSVERLAUF|BEKANNTE-GRENZEN|PRUEFPROTOKOLL)\.md$',
    '^(SHA256SUMS\.txt|distribution-manifest\.json)$'
)

# Things that must never be shipped, with the reason shown to the person who reads the report.
$script:DeniedFile = @(
    @{ Regex = '\.(msi|msp|msix)$'; Reason = 'vendor installer' },
    @{ Regex = '\.intunewin$'; Reason = 'built package' },
    @{ Regex = '(^|/)IntuneWinAppUtil\.exe$'; Reason = 'Content Prep Tool (license forbids redistribution)' },
    @{ Regex = '(^|/)(project|configuration|build-manifest|source-manifest|settings)\.json$'; Reason = 'user project or settings file' },
    @{ Regex = '(^|/)(builds|versions|source)/'; Reason = 'user project folder' },
    @{ Regex = '\.(pdb|trx)$'; Reason = 'debug or test artifact' },
    @{ Regex = '(\.Tests\.dll$|(^|/)(xunit|FlaUI|Interop\.UIAutomation|Microsoft\.TestPlatform|Microsoft\.VisualStudio\.TestPlatform)[^/]*$)'; Reason = 'test assembly or test framework' },
    @{ Regex = '(^|/)\.git'; Reason = 'repository metadata' }
)

function Get-Sha256Text {
    param([Parameter(Mandatory = $true)][string]$Path)
    $stream = [System.IO.File]::OpenRead($Path)
    try {
        $sha = [System.Security.Cryptography.SHA256]::Create()
        try {
            return ([System.BitConverter]::ToString($sha.ComputeHash($stream))).Replace('-', '').ToLowerInvariant()
        }
        finally {
            $sha.Dispose()
        }
    }
    finally {
        $stream.Dispose()
    }
}

function Get-SignatureStatusText {
    # 'Valid' for a valid Authenticode signature; anything else (not signed, unreadable) counts as not signed.
    param([Parameter(Mandatory = $true)][string]$Path)
    try {
        return (Get-AuthenticodeSignature -LiteralPath $Path -ErrorAction Stop).Status.ToString()
    }
    catch {
        return 'NotSigned'
    }
}

function Get-RelativeFile {
    # All files below a folder as @{ Relative; Full } with forward slashes in Relative.
    param([Parameter(Mandatory = $true)][string]$Folder)
    $root = [System.IO.Path]::GetFullPath($Folder).TrimEnd('\')
    foreach ($file in [System.IO.Directory]::GetFiles($root, '*', [System.IO.SearchOption]::AllDirectories)) {
        [pscustomobject]@{ Relative = $file.Substring($root.Length + 1).Replace('\', '/'); Full = $file }
    }
}

function Get-DistributionProblem {
    <#
    .SYNOPSIS
    Checks an unpacked distribution folder and returns one text per problem (an empty result means the folder passes).
    #>
    param([Parameter(Mandatory = $true)][string]$Folder)

    $problems = New-Object System.Collections.Generic.List[string]
    if (-not (Test-Path -LiteralPath $Folder -PathType Container)) {
        $problems.Add("Folder not found: $Folder")
        return $problems.ToArray()
    }

    $files = @(Get-RelativeFile -Folder $Folder)
    foreach ($file in $files) {
        $denied = $null
        foreach ($rule in $script:DeniedFile) {
            if ($file.Relative -match $rule.Regex) { $denied = $rule.Reason; break }
        }
        if ($denied) {
            $problems.Add("Forbidden file '$($file.Relative)': $denied")
            continue
        }
        $allowed = $false
        foreach ($pattern in $script:AllowedFile) {
            if ($file.Relative -match $pattern) { $allowed = $true; break }
        }
        if (-not $allowed) { $problems.Add("File not on the allow list: '$($file.Relative)'") }
    }

    $byName = @{}
    foreach ($file in $files) { $byName[$file.Relative.ToLowerInvariant()] = $file }

    # The checksums must cover every file except the checksum file itself, and every line must match the real file.
    $sumsFile = $byName['sha256sums.txt']
    if (-not $sumsFile) {
        $problems.Add('SHA256SUMS.txt is missing')
    }
    else {
        $listed = @{}
        foreach ($line in [System.IO.File]::ReadAllLines($sumsFile.Full)) {
            if ([string]::IsNullOrWhiteSpace($line)) { continue }
            if ($line -notmatch '^([0-9a-f]{64}) \*(.+)$') {
                $problems.Add("Malformed line in SHA256SUMS.txt: '$line'")
                continue
            }
            $hash = $Matches[1]
            $name = $Matches[2]
            $listed[$name.ToLowerInvariant()] = $true
            $actual = $byName[$name.ToLowerInvariant()]
            if (-not $actual) {
                $problems.Add("SHA256SUMS.txt lists a file that is not there: '$name'")
            }
            elseif ((Get-Sha256Text -Path $actual.Full) -ne $hash) {
                $problems.Add("Checksum mismatch: '$name'")
            }
        }
        foreach ($file in $files) {
            if ($file.Relative -ieq 'SHA256SUMS.txt') { continue }
            if (-not $listed.ContainsKey($file.Relative.ToLowerInvariant())) {
                $problems.Add("File without checksum: '$($file.Relative)'")
            }
        }
    }

    # The manifest must exist and must not claim a signature that is not there (SPEC section 4).
    $manifestFile = $byName['distribution-manifest.json']
    if (-not $manifestFile) {
        $problems.Add('distribution-manifest.json is missing')
    }
    else {
        $manifest = $null
        try {
            $manifest = [System.IO.File]::ReadAllText($manifestFile.Full) | ConvertFrom-Json
        }
        catch {
            $problems.Add("distribution-manifest.json is not valid JSON: $($_.Exception.Message)")
        }
        if ($manifest) {
            $exe = $byName['intunepackagebuilder.exe']
            if ($exe) {
                $actualStatus = Get-SignatureStatusText -Path $exe.Full
                $declared = ''
                if ($manifest.PSObject.Properties['signatureStatus']) { $declared = [string]$manifest.signatureStatus }
                $isSigned = $actualStatus -eq 'Valid'
                if (-not $isSigned -and $declared -ne 'unsigned') {
                    $problems.Add("The program is not signed (status $actualStatus) but the manifest says '$declared'")
                }
                if ($isSigned -and $declared -eq 'unsigned') {
                    $problems.Add('The program is signed but the manifest says unsigned')
                }
            }
        }
    }

    # Every shipped third-party library must be documented with the checksum of the shipped file (SPEC section 11).
    $thirdParty = $byName['third-party.md']
    if (-not $thirdParty) {
        $problems.Add('THIRD-PARTY.md is missing')
    }
    else {
        $text = [System.IO.File]::ReadAllText($thirdParty.Full)
        $newtonsoft = $byName['newtonsoft.json.dll']
        if ($newtonsoft) {
            $hash = Get-Sha256Text -Path $newtonsoft.Full
            if ($text.IndexOf($hash, [System.StringComparison]::OrdinalIgnoreCase) -lt 0) {
                $problems.Add("THIRD-PARTY.md does not document the SHA-256 of the shipped Newtonsoft.Json.dll ($hash)")
            }
        }
    }

    return $problems.ToArray()
}

function New-DistributionFolder {
    <#
    .SYNOPSIS
    Copies the allow-listed files of a program build into a distribution folder and writes the checksums and the manifest.
    #>
    param(
        [Parameter(Mandatory = $true)][string]$BuildOutput,
        [Parameter(Mandatory = $true)][string]$RepositoryRoot,
        [Parameter(Mandatory = $true)][string]$Destination,
        [Parameter(Mandatory = $true)][string]$Version
    )

    if (Test-Path -LiteralPath $Destination) {
        throw "The destination already exists and is not overwritten: $Destination"
    }
    [void][System.IO.Directory]::CreateDirectory($Destination)
    $copied = New-Object System.Collections.Generic.List[string]

    function Copy-IfAllowed {
        param([string]$Source, [string]$Relative)
        $target = [System.IO.Path]::Combine($Destination, $Relative.Replace('/', '\'))
        [void][System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($target))
        [System.IO.File]::Copy($Source, $target)
        $copied.Add($Relative)
    }

    # Program files come from the build output: only what the allow list names, the rest (pdb, xml docs, ...) stays behind.
    foreach ($file in @(Get-RelativeFile -Folder $BuildOutput)) {
        foreach ($pattern in $script:AllowedFile) {
            if ($file.Relative -match $pattern -and $file.Relative -notmatch '^(LICENSE|THIRD-PARTY|README|docs/|SHA256SUMS|distribution-manifest)') {
                Copy-IfAllowed -Source $file.Full -Relative $file.Relative
                break
            }
        }
    }

    # Documents come from the repository.
    foreach ($relative in 'LICENSE', 'THIRD-PARTY.md', 'README.md', 'README.en.md') {
        $source = [System.IO.Path]::Combine($RepositoryRoot, $relative)
        if (Test-Path -LiteralPath $source) { Copy-IfAllowed -Source $source -Relative $relative }
    }
    foreach ($name in 'BEDIENUNG', 'DATENFORMAT', 'AENDERUNGSVERLAUF', 'BEKANNTE-GRENZEN', 'PRUEFPROTOKOLL') {
        $source = [System.IO.Path]::Combine($RepositoryRoot, 'docs', ($name + '.md'))
        if (Test-Path -LiteralPath $source) { Copy-IfAllowed -Source $source -Relative ('docs/' + $name + '.md') }
    }

    $exePath = [System.IO.Path]::Combine($Destination, 'IntunePackageBuilder.exe')
    $status = 'unsigned'
    if (Test-Path -LiteralPath $exePath) {
        $signature = Get-SignatureStatusText -Path $exePath
        if ($signature -eq 'Valid') { $status = 'signed' }
    }

    $entries = @()
    foreach ($relative in ($copied | Sort-Object)) {
        $full = [System.IO.Path]::Combine($Destination, $relative.Replace('/', '\'))
        $entries += [pscustomobject]@{
            path   = $relative
            size   = (New-Object System.IO.FileInfo $full).Length
            sha256 = Get-Sha256Text -Path $full
        }
    }
    $manifest = [pscustomobject]@{
        product         = 'Intune Package Builder'
        version         = $Version
        signatureStatus = $status
        note            = 'The Win32 Content Prep Tool is not part of this distribution; users supply it themselves (THIRD-PARTY.md).'
        files           = $entries
    }
    $utf8 = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText([System.IO.Path]::Combine($Destination, 'distribution-manifest.json'), ($manifest | ConvertTo-Json -Depth 5), $utf8)

    # The checksum file covers every file except itself, including the manifest.
    $lines = @()
    foreach ($file in (Get-RelativeFile -Folder $Destination | Sort-Object Relative)) {
        $lines += ('{0} *{1}' -f (Get-Sha256Text -Path $file.Full), $file.Relative)
    }
    [System.IO.File]::WriteAllText([System.IO.Path]::Combine($Destination, 'SHA256SUMS.txt'), (($lines -join "`n") + "`n"), $utf8)
}

Export-ModuleMember -Function Get-Sha256Text, Get-DistributionProblem, New-DistributionFolder
