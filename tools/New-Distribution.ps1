<#
.SYNOPSIS
Builds the portable distribution ZIP from a Release build of the program and verifies it (acceptance check A18).

.EXAMPLE
.\tools\New-Distribution.ps1 -BuildOutput src\IntunePackageBuilder.App\bin\Release\net48 -OutputDirectory dist
#>
param(
    [Parameter(Mandatory = $true)][string]$BuildOutput,
    [Parameter(Mandatory = $true)][string]$OutputDirectory,
    [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$Version
)

$ErrorActionPreference = 'Stop'
Import-Module ([System.IO.Path]::Combine($PSScriptRoot, 'Distribution.psm1')) -Force

if (-not $Version) {
    $exe = [System.IO.Path]::Combine($BuildOutput, 'IntunePackageBuilder.exe')
    $v = [System.Reflection.AssemblyName]::GetAssemblyName($exe).Version
    $Version = '{0}.{1}.{2}' -f $v.Major, $v.Minor, $v.Build
}

$name = "IntunePackageBuilder-$Version"
[void][System.IO.Directory]::CreateDirectory($OutputDirectory)
$folder = [System.IO.Path]::Combine($OutputDirectory, $name)
$zip = [System.IO.Path]::Combine($OutputDirectory, $name + '.zip')
if (Test-Path -LiteralPath $zip) { throw "The ZIP already exists and is not overwritten: $zip" }

New-DistributionFolder -BuildOutput $BuildOutput -RepositoryRoot $RepositoryRoot -Destination $folder -Version $Version

Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($folder, $zip, [System.IO.Compression.CompressionLevel]::Optimal, $true)

# The check runs on what was actually zipped, not on the staging folder.
& ([System.IO.Path]::Combine($PSScriptRoot, 'Test-Distribution.ps1')) -Path $zip
if ($null -ne $LASTEXITCODE -and $LASTEXITCODE -ne 0) { throw 'The distribution check failed; the ZIP must not be released.' }
Write-Output $zip
