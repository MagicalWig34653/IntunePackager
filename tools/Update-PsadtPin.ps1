<#
.SYNOPSIS
Checks for a newer PSAppDeployToolkit release and records it in deploy/psadt/psadt.json and THIRD-PARTY.md.
Used by .github/workflows/update-psadt.yml; it never merges anything and downloads the release ZIP only to compute its SHA-256.
#>
param(
    [string]$Repository = 'PSAppDeployToolkit/PSAppDeployToolkit',
    [string]$AssetName = 'PSAppDeployToolkit_Template_v4.zip',
    [string]$PinPath = ([System.IO.Path]::Combine($PSScriptRoot, '..', 'deploy', 'psadt', 'psadt.json')),
    [string]$ThirdPartyPath = ([System.IO.Path]::Combine($PSScriptRoot, '..', 'THIRD-PARTY.md')),
    [string]$PullRequestBodyPath
)

$ErrorActionPreference = 'Stop'
Import-Module ([System.IO.Path]::Combine($PSScriptRoot, 'Psadt.psm1')) -Force
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Set-WorkflowOutput([string]$Name, [string]$Value) {
    Write-Host "$Name=$Value"
    if ($env:GITHUB_OUTPUT) { [System.IO.File]::AppendAllText($env:GITHUB_OUTPUT, "$Name=$Value`n") }
}

$headers = @{ 'User-Agent' = 'intune-package-builder-update-psadt'; 'Accept' = 'application/vnd.github+json' }
if ($env:GH_TOKEN) { $headers['Authorization'] = 'Bearer ' + $env:GH_TOKEN }

$release = Invoke-RestMethod -Headers $headers -Uri "https://api.github.com/repos/$Repository/releases/latest"
if ($release.draft -or $release.prerelease) { throw "The latest release $($release.tag_name) is a draft or pre-release." }
$version = ConvertTo-PsadtVersion -Tag $release.tag_name

$pinText = [System.IO.File]::ReadAllText($PinPath)
$pin = $pinText | ConvertFrom-Json
if (@($pin.versions | Where-Object { $_.version -eq $version }).Count -gt 0) {
    Write-Host "PSAppDeployToolkit $version is already recorded."
    Set-WorkflowOutput 'changed' 'false'
    exit 0
}

$asset = @($release.assets | Where-Object { $_.name -eq $AssetName }) | Select-Object -First 1
if (-not $asset) { throw "Release $($release.tag_name) has no asset named $AssetName." }

$temp = [System.IO.Path]::Combine([System.IO.Path]::GetTempPath(), 'ipb-psadt-' + [System.Guid]::NewGuid().ToString('N'))
[System.IO.Directory]::CreateDirectory($temp) | Out-Null
try {
    $zipPath = [System.IO.Path]::Combine($temp, $AssetName)
    Invoke-WebRequest -Headers @{ 'User-Agent' = 'intune-package-builder-update-psadt' } -Uri $asset.browser_download_url -OutFile $zipPath -UseBasicParsing
    $sha256 = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
    if (-not $asset.digest) { throw "GitHub reports no digest for $AssetName; the download cannot be cross-checked." }
    if ($asset.digest -ne "sha256:$sha256") {
        throw "The downloaded ZIP has SHA-256 $sha256 but GitHub reports $($asset.digest)."
    }

    $zip = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
    try {
        $entryNames = @($zip.Entries | ForEach-Object { $_.FullName })
        $licenseEntry = $zip.GetEntry('PSAppDeployToolkit/COPYING.Lesser')
        if (-not $licenseEntry) { throw 'The ZIP contains no PSAppDeployToolkit/COPYING.Lesser.' }
        $stream = $licenseEntry.Open()
        try {
            $sha = [System.Security.Cryptography.SHA256]::Create()
            $licenseSha256 = ([System.BitConverter]::ToString($sha.ComputeHash($stream))).Replace('-', '').ToLowerInvariant()
        }
        finally { $stream.Dispose() }
    }
    finally { $zip.Dispose() }
}
finally {
    Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
}

$commit = (Invoke-RestMethod -Headers $headers -Uri "https://api.github.com/repos/$Repository/commits/$($release.tag_name)").sha
if ($commit -notmatch '^[0-9a-f]{40}$') { throw "Unexpected commit id '$commit' for $($release.tag_name)." }
$libraries = @(Get-PsadtBundledLibrary -EntryName $entryNames)
$previous = @($pin.versions | Select-Object -First 1)[0]
$newLibraries = @($libraries | Where-Object { @($previous.bundledLibraries) -notcontains $_ })
$licenseChanged = ($licenseSha256 -ne $pin.licenseSha256)

$entry = [ordered]@{
    version          = $version
    asset            = $AssetName
    sha256           = $sha256
    commit           = $commit
    bundledLibraries = $libraries
}
$updated = Add-PsadtPinVersion -PinJson $pinText -Entry $entry
[System.IO.File]::WriteAllText($PinPath, $updated, (New-Object System.Text.UTF8Encoding($false)))

$thirdParty = [System.IO.File]::ReadAllText($ThirdPartyPath)
$thirdPartyUpdated = Update-PsadtThirdPartyRow -Text $thirdParty -Version $version -Commit $commit -Sha256 $sha256
[System.IO.File]::WriteAllText($ThirdPartyPath, $thirdPartyUpdated, (New-Object System.Text.UTF8Encoding($false)))

if ($PullRequestBodyPath) {
    $body = New-PsadtPullRequestBody -Version $version -Tag $release.tag_name -Sha256 $sha256 -Commit $commit -NewLibrary $newLibraries -LicenseChanged $licenseChanged
    [System.IO.File]::WriteAllText($PullRequestBodyPath, $body, (New-Object System.Text.UTF8Encoding($false)))
}

Set-WorkflowOutput 'changed' 'true'
Set-WorkflowOutput 'version' $version
Set-WorkflowOutput 'license_changed' ([string]$licenseChanged).ToLowerInvariant()
Set-WorkflowOutput 'new_libraries' ([string]$newLibraries.Count)
exit 0
