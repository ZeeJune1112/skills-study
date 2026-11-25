[CmdletBinding()]
param([string] $DotNetPath)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'dotnet.ps1')

$dotnet = Resolve-GomokuDotNet $DotNetPath
$repoRoot = Split-Path -Parent $PSScriptRoot
Push-Location -LiteralPath $repoRoot
try {
    "Using .NET SDK $(& $dotnet --version) at $dotnet"
    Invoke-GomokuDotNet $dotnet @('build', 'Gomoku.slnx', '-c', 'Release')
    Invoke-GomokuDotNet $dotnet @('run', '--project', 'tests/Gomoku.Checks', '-c', 'Release', '--no-build')
} finally {
    Pop-Location
}
