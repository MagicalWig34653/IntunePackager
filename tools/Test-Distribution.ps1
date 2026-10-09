<#
.SYNOPSIS
Acceptance check A18: only released files, correct checksums, no projects, vendor installers or test artifacts.
Accepts a distribution ZIP or an unpacked distribution folder and fails (exit code 1) when there is any problem.
#>
param([Parameter(Mandatory = $true)][string]$Path)

$ErrorActionPreference = 'Stop'
Import-Module ([System.IO.Path]::Combine($PSScriptRoot, 'Distribution.psm1')) -Force

$folder = $Path
$temp = $null
if (Test-Path -LiteralPath $Path -PathType Leaf) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $temp = [System.IO.Path]::Combine([System.IO.Path]::GetTempPath(), 'ipb-dist-' + [System.Guid]::NewGuid().ToString('N'))
    [System.IO.Compression.ZipFile]::ExtractToDirectory($Path, $temp)
    $roots = @([System.IO.Directory]::GetDirectories($temp))
    $files = @([System.IO.Directory]::GetFiles($temp))
    # A ZIP made by New-Distribution.ps1 has exactly one top-level folder; anything next to it is a problem.
    if ($roots.Count -eq 1 -and $files.Count -eq 0) { $folder = $roots[0] } else { $folder = $temp }
}

try {
    $problems = @(Get-DistributionProblem -Folder $folder)
}
finally {
    if ($temp) { Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue }
}

if ($problems.Count -gt 0) {
    foreach ($problem in $problems) { Write-Error $problem -ErrorAction Continue }
    Write-Output ("Distribution check FAILED with {0} problem(s)." -f $problems.Count)
    exit 1
}
Write-Output 'Distribution check passed.'
exit 0
