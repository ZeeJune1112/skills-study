[CmdletBinding()]
param([string] $DotNetPath)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'dotnet.ps1')

$dotnet = Resolve-GomokuDotNet $DotNetPath
$repoRoot = Split-Path -Parent $PSScriptRoot
$dist = Join-Path $repoRoot 'dist'
$release = Join-Path $dist 'win-x64'
$target = Join-Path $release 'Gomoku.exe'
$staging = Join-Path $dist ".gomoku-publish-$([Guid]::NewGuid().ToString('N'))"

New-Item -Path $dist -ItemType Directory -Force | Out-Null
$dist = (Resolve-Path -LiteralPath $dist).ProviderPath
$lock = [IO.File]::Open((Join-Path $dist '.publish.lock'), 'OpenOrCreate', 'ReadWrite', 'None')
Push-Location -LiteralPath $repoRoot
try {
    if (Test-Path -LiteralPath $release -PathType Container) {
        $extras = @(Get-ChildItem -LiteralPath $release -Force | Where-Object { $_.PSIsContainer -or $_.Name -ne 'Gomoku.exe' })
        if ($extras.Count) { throw "The release directory contains unexpected entries: $($extras.Name -join ', ')." }
    }

    & (Join-Path $PSScriptRoot 'check.ps1') -DotNetPath $dotnet
    New-Item -Path $staging -ItemType Directory | Out-Null
    Invoke-GomokuDotNet $dotnet @('publish', 'src/Gomoku.App', '-c', 'Release', '-r', 'win-x64',
        '--self-contained', '-p:PublishProfile=WindowsX64', '-o', $staging)

    $files = @(Get-ChildItem -LiteralPath $staging -Force)
    if ($files.Count -ne 1 -or $files[0].PSIsContainer -or $files[0].Name -ne 'Gomoku.exe') {
        throw 'Publishing must produce only Gomoku.exe.'
    }

    $candidate = $files[0].FullName
    $hash = (Get-FileHash -LiteralPath $candidate -Algorithm SHA256).Hash
    & (Join-Path $repoRoot 'tests/Windows/smoke.ps1') -Executable $candidate
    if ((Get-FileHash -LiteralPath $candidate -Algorithm SHA256).Hash -ne $hash) {
        throw 'The candidate changed during testing.'
    }

    New-Item -Path $release -ItemType Directory -Force | Out-Null
    if (Test-Path -LiteralPath $target -PathType Leaf) {
        [IO.File]::Replace($candidate, $target, [NullString]::Value)
    } else {
        [IO.File]::Move($candidate, $target)
    }

    if ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ne $hash) {
        throw 'The released executable differs from the tested candidate.'
    }

    "Published $target"
    "SHA256 $hash"
} finally {
    try {
        if (Test-Path -LiteralPath $staging -PathType Container) {
            $resolved = (Resolve-Path -LiteralPath $staging).ProviderPath
            if ((Split-Path -Parent $resolved) -eq $dist -and
                (Split-Path -Leaf $resolved) -like '.gomoku-publish-*' -and
                -not ((Get-Item -LiteralPath $resolved -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
                Remove-Item -LiteralPath $resolved -Recurse -Force
            }
        }
    } finally {
        Pop-Location
        $lock.Dispose()
    }
}
