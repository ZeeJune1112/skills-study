function Resolve-GomokuDotNet {
    param([string] $DotNetPath)

    $repoRoot = Split-Path -Parent $PSScriptRoot
    $candidates = if ($DotNetPath) {
        (Resolve-Path -LiteralPath $DotNetPath -ErrorAction Stop).ProviderPath
    } else {
        Get-Command dotnet.exe -CommandType Application -All -ErrorAction SilentlyContinue |
            Select-Object -ExpandProperty Source
        if ($env:LOCALAPPDATA) {
            Join-Path $env:LOCALAPPDATA 'GomokuBuildTools\dotnet\dotnet.exe'
        }
    }

    Push-Location -LiteralPath $repoRoot
    try {
        foreach ($candidate in $candidates) {
            if (Test-Path -LiteralPath $candidate -PathType Leaf) {
                $version = & $candidate --version 2>$null
                if ($LASTEXITCODE -eq 0 -and $version -match '^10\.\d+\.\d+$') {
                    return $candidate
                }
            }
        }
    } finally {
        Pop-Location
    }

    throw 'The .NET 10 SDK is required. Supply -DotNetPath with the path to its dotnet.exe.'
}

function Invoke-GomokuDotNet {
    param([string] $DotNetPath, [string[]] $CommandArguments)

    & $DotNetPath @CommandArguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($CommandArguments[0]) failed with exit code $LASTEXITCODE."
    }
}
