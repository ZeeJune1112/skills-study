[CmdletBinding()]
param(
    [string] $Executable,
    [ValidateSet('All', 'Ai', 'GuestExitRoom', 'GuestClose', 'GuestImmediate', 'GuestExpired',
        'SpectatorExitRoom', 'SpectatorClose', 'HostClose', 'CompactLayout')]
    [string] $Scenario = 'All'
)

$ErrorActionPreference = 'Stop'
if (!$Executable) { $Executable = Join-Path $PSScriptRoot '../../dist/win-x64/Gomoku.exe' }
$Executable = (Resolve-Path -LiteralPath $Executable).ProviderPath
. (Join-Path $PSScriptRoot 'SmokeTools.ps1')

function Test-GomokuCompactLayout {
    param([IntPtr] $Owner, [IntPtr] $Player, [IntPtr] $Viewer)

    $previousDpi = [GomokuSmokeWindows]::SetThreadDpiAwarenessContext([IntPtr]::new(-4))
    try {
        foreach ($window in @($Owner, $Player, $Viewer)) {
            $initial = [GomokuSmokeWindows]::Bounds($window)
            [GomokuSmokeWindows]::Resize($window, 100, 100)
            $first = [GomokuSmokeWindows]::Bounds($window)
            [GomokuSmokeWindows]::Resize($window, 1, 1)
            $minimum = [GomokuSmokeWindows]::Bounds($window)
            if ($minimum[2] -ne $first[2] -or $minimum[3] -ne $first[3] -or
                $minimum[2] -le 100 -or $minimum[3] -le 100 -or
                $minimum[2] -ge $initial[2] -or $minimum[3] -ge $initial[3]) {
                throw "Did not reach a smaller clamped window size. Initial: $($initial[2])x$($initial[3]). Observed: $($minimum[2])x$($minimum[3])."
            }
            Wait-GomokuControl $window -Text '退出房间' -Fits -Enabled | Out-Null
        }

        Invoke-GomokuIntersection $Owner 7 7
        Wait-GomokuControl $Player -Text '轮到你落子' | Out-Null
        $edit = Wait-GomokuControl $Viewer -ClassName 'WindowsForms10.EDIT.*' -Fits -Enabled
        $send = Wait-GomokuControl $Viewer -Text '发送弹幕' -Fits -Enabled
        [GomokuSmokeWindows]::SetText($edit.Handle, '继续下棋')
        [GomokuSmokeWindows]::Press($send.Handle, $false)
        Invoke-GomokuButton $Owner '请求悔棋'
        Wait-GomokuControl $Player -Text '对手请求悔棋。' -Fits | Out-Null
        Wait-GomokuControl $Player -Text '拒绝' -Fits -Enabled | Out-Null
        $confirm = Wait-GomokuControl $Player -Text '同意' -Fits -Enabled
        [GomokuSmokeWindows]::Press($confirm.Handle, $false)
        Wait-GomokuControl $Owner -Text '已同意悔棋。' | Out-Null
        Invoke-GomokuButton $Viewer '退出房间' -Asynchronous
        Wait-GomokuControl $Viewer -Text '创建双人房' -Enabled | Out-Null
        "Clamped window: $($minimum[2])x$($minimum[3]); default: $($initial[2])x$($initial[3])."
    } finally {
        [GomokuSmokeWindows]::SetThreadDpiAwarenessContext($previousDpi) | Out-Null
    }
}

function Test-GomokuReentry {
    param($Clients, $Desktop, [string] $Case, [bool] $WithViewer)

    $owner = $Clients[0].Window
    $player = $Clients[1].Window
    $spectatorTest = $Case.StartsWith('Spectator')
    $waitSeconds = if ($Case -eq 'GuestExpired') { 15 } else { 30 }
    $roomId = New-GomokuRoom $Clients[0] $Desktop -Capacity 1 -WaitSeconds $waitSeconds -Immediate:($Case -eq 'GuestImmediate')
    Start-GomokuDuel $owner $player $roomId
    if ($WithViewer) {
        $viewer = $Clients[2].Window
        Join-GomokuRoom $viewer $roomId
        Wait-GomokuControl $viewer -Text '你在观众席' | Out-Null
        Wait-GomokuControl $owner -Text '1/1' | Out-Null
    }
    if ($Case -eq 'CompactLayout') {
        Test-GomokuCompactLayout $owner $player $viewer
        return
    }

    Invoke-GomokuIntersection $owner 7 7
    Wait-GomokuControl $player -Text '轮到你落子' | Out-Null
    if ($Case -eq 'HostClose') {
        [GomokuSmokeWindows]::RequestClose($owner)
        Wait-GomokuControl $player -Text '房主已关闭房间。' | Out-Null
        return
    }

    $returning = if ($spectatorTest) { $Clients[2] } else { $Clients[1] }
    $closeWindow = $Case.EndsWith('Close')
    if ($closeWindow) {
        [GomokuSmokeWindows]::RequestClose($returning.Window)
        if (!$returning.Process.WaitForExit(5000)) { throw 'The test client did not exit.' }
    } else {
        Invoke-GomokuButton $returning.Window '退出房间' -Asynchronous
        Wait-GomokuControl $returning.Window -Text '创建双人房' -Enabled | Out-Null
    }

    if ($spectatorTest) {
        Wait-GomokuControl $owner -Text '0/1' | Out-Null
        Invoke-GomokuIntersection $player 7 8
        Wait-GomokuControl $owner -Text '轮到你落子' | Out-Null
    } elseif ($Case -ne 'GuestImmediate') {
        Wait-GomokuControl $owner -Text '*暂停对局*' | Out-Null
        if ($Case -eq 'GuestExpired') {
            Wait-GomokuControl $owner -Text '对局已中止，等待重连超时。' -Seconds 18 | Out-Null
        }
    }

    if ($closeWindow) { $returning = Start-GomokuSmokeClient $Desktop }
    Join-GomokuRoom $returning.Window $roomId
    if ($Case -in @('GuestImmediate', 'GuestExpired')) {
        Wait-GomokuControl $returning.Window -Text '你在观众席' | Out-Null
        Wait-GomokuControl $returning.Window -Text '对局已中止*' | Out-Null
        Wait-GomokuControl $owner -Text '空席' | Out-Null
        return
    }
    if ($spectatorTest) {
        Wait-GomokuControl $returning.Window -Text '你在观众席' | Out-Null
        Wait-GomokuControl $returning.Window -Text '轮到黑棋' | Out-Null
        Wait-GomokuControl $owner -Text '1/1' | Out-Null
        Invoke-GomokuIntersection $owner 8 8
        Wait-GomokuControl $returning.Window -Text '轮到白棋' | Out-Null
        Wait-GomokuControl $owner -Text '轮到白棋' | Out-Null
        Assert-GomokuDanmaku $owner $returning.Window
        return
    }

    Wait-GomokuControl $returning.Window -Text '你执白' | Out-Null
    Wait-GomokuControl $returning.Window -Text '轮到你落子' | Out-Null
    Invoke-GomokuIntersection $returning.Window 7 8
    Wait-GomokuControl $owner -Text '轮到你落子' | Out-Null
    if ($WithViewer) {
        Wait-GomokuControl $viewer -Text '轮到黑棋' | Out-Null
        Wait-GomokuControl $viewer -Text '1/1' | Out-Null
    }
}

$cases = if ($Scenario -eq 'All') {
    'Ai', 'GuestExitRoom', 'GuestClose', 'GuestImmediate', 'GuestExpired',
    'SpectatorExitRoom', 'SpectatorClose', 'HostClose', 'CompactLayout'
} else {
    $Scenario
}

foreach ($case in $cases) {
    $withViewer = $case.StartsWith('Spectator') -or $case -in @('GuestClose', 'CompactLayout')
    $clientCount = if ($withViewer) { 3 } elseif ($case -eq 'Ai') { 1 } else { 2 }
    try {
        Invoke-GomokuDesktopTest -Executable $Executable -ClientCount $clientCount -Test {
            param($clients, $desktop)
            if ($case -eq 'Ai') {
                $owner = $clients[0].Window
                New-GomokuRoom $clients[0] $desktop -Ai | Out-Null
                Wait-GomokuControl $owner -Text '轮到你落子' | Out-Null
                Invoke-GomokuIntersection $owner 7 7
                Wait-GomokuControl $owner -Text '悔棋' -Enabled | Out-Null
                Wait-GomokuControl $owner -Text '轮到你落子' | Out-Null
                Invoke-GomokuButton $owner '悔棋'
                Invoke-GomokuButton $owner '退出房间' -Asynchronous
                Wait-GomokuControl $owner -Text '创建人机房' -Enabled | Out-Null
            } else {
                Test-GomokuReentry $clients $desktop $case $withViewer
            }
        }
    } catch {
        throw "FAIL ${case}: $($_.Exception.Message)"
    }
    "PASS $case"
}
