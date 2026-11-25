if (-not ('GomokuSmokeWindows' -as [type])) {
    Add-Type -Path (Join-Path $PSScriptRoot 'NativeWindows.cs')
}
Add-Type -AssemblyName System.Drawing

function Wait-GomokuWindow {
    param([Diagnostics.Process] $Process, [IntPtr] $Desktop, [string] $Title = '五子棋')

    $timer = [Diagnostics.Stopwatch]::StartNew()
    do {
        if ($Process.HasExited) { throw "Gomoku exited with code $($Process.ExitCode)." }
        $window = [GomokuSmokeWindows]::FindWindow($Process.Id, $Title, $Desktop)
        if ($window -ne [IntPtr]::Zero) { return $window }
        Start-Sleep -Milliseconds 100
    } while ($timer.Elapsed.TotalSeconds -lt 20)
    throw "The test window '$Title' did not open."
}

function Wait-GomokuControl {
    param([IntPtr] $Window, [string] $Text = '*', [string] $ClassName = '*',
        [int] $Seconds = 8, [switch] $Enabled, [switch] $Fits)

    $timer = [Diagnostics.Stopwatch]::StartNew()
    do {
        $controls = [GomokuSmokeWindows]::Children($Window)
        $control = $controls | Where-Object {
            $_.Visible -and $_.Text -like $Text -and $_.ClassName -like $ClassName -and
            (!$Enabled -or $_.Enabled) -and (!$Fits -or [GomokuSmokeWindows]::Fits($_.Handle))
        } | Select-Object -First 1
        if ($control) { return $control }
        Start-Sleep -Milliseconds 80
    } while ($timer.Elapsed.TotalSeconds -lt $Seconds)
    $visibleText = ($controls | Where-Object { $_.Visible -and $_.Text }).Text -join ' / '
    throw "Control '$Text' ($ClassName) did not become ready. Visible text: $visibleText"
}

function Invoke-GomokuButton {
    param([IntPtr] $Window, [string] $Text, [switch] $Asynchronous)

    $button = Wait-GomokuControl $Window -Text $Text -Enabled
    [GomokuSmokeWindows]::Press($button.Handle, [bool] $Asynchronous)
}

function Start-GomokuSmokeClient {
    param($Desktop)

    $directory = Split-Path -Parent $Desktop.Executable
    $processId = [GomokuSmokeWindows]::StartOnDesktop($Desktop.Executable, $directory, $Desktop.Name)
    $process = Get-Process -Id $processId
    $Desktop.Processes.Add($process)
    $window = Wait-GomokuWindow $process $Desktop.Handle
    Wait-GomokuControl $window -Text '创建双人房' -Enabled -Seconds 15 | Out-Null
    [pscustomobject] @{ Window = $window; Process = $process }
}

function Invoke-GomokuDesktopTest {
    param([string] $Executable, [int] $ClientCount, [scriptblock] $Test)

    $name = "GomokuSmoke-$PID-$([Guid]::NewGuid().ToString('N'))"
    $desktop = [pscustomobject] @{
        Name = $name
        Handle = [GomokuSmokeWindows]::OpenDesktop($name)
        Executable = $Executable
        Processes = [Collections.Generic.List[Diagnostics.Process]]::new()
    }

    $previousDotNetRoot = $env:DOTNET_ROOT
    $previousDotNetRootX64 = $env:DOTNET_ROOT_X64
    try {
        $env:DOTNET_ROOT = Join-Path (Split-Path -Parent $Executable) $name
        $env:DOTNET_ROOT_X64 = $env:DOTNET_ROOT
        $clients = @(foreach ($number in 1..$ClientCount) { Start-GomokuSmokeClient $desktop })
        & $Test $clients $desktop
    } finally {
        try {
            foreach ($process in $desktop.Processes) {
                if (!$process.HasExited) {
                    Stop-Process -InputObject $process -ErrorAction SilentlyContinue
                    $process.WaitForExit(5000) | Out-Null
                }
                $process.Dispose()
            }
        } finally {
            $env:DOTNET_ROOT = $previousDotNetRoot
            $env:DOTNET_ROOT_X64 = $previousDotNetRootX64
            [GomokuSmokeWindows]::CloseDesktop($desktop.Handle) | Out-Null
        }
    }
}

function New-GomokuRoom {
    param($Client, $Desktop, [int] $Capacity = 5, [int] $WaitSeconds = 30, [switch] $Immediate, [switch] $Ai)

    $title = if ($Ai) { '创建人机房' } else { '创建双人房' }
    Invoke-GomokuButton $Client.Window $title -Asynchronous
    $dialog = Wait-GomokuWindow $Client.Process $Desktop.Handle $title
    $editClass = 'WindowsForms10.EDIT.*'
    if ($Capacity -ne 5) {
        $capacityInput = Wait-GomokuControl $dialog -Text '5' -ClassName $editClass
        [GomokuSmokeWindows]::SetText($capacityInput.Handle, [string] $Capacity)
    }
    if ($WaitSeconds -ne 30) {
        $waitInput = Wait-GomokuControl $dialog -Text '30' -ClassName $editClass
        [GomokuSmokeWindows]::SetText($waitInput.Handle, [string] $WaitSeconds)
    }
    if ($Immediate) {
        $picker = Wait-GomokuControl $dialog -ClassName 'WindowsForms10.COMBOBOX.*'
        [GomokuSmokeWindows]::SelectIndex($picker.Handle, 0, $true)
    }

    Invoke-GomokuButton $dialog $title
    return ((Wait-GomokuControl $Client.Window -Text '房间标识*').Text -split '\s+')[-1]
}

function Join-GomokuRoom {
    param([IntPtr] $Window, [string] $RoomId)

    $roomList = Wait-GomokuControl $Window -ClassName 'WindowsForms10.LISTBOX.*'
    $timer = [Diagnostics.Stopwatch]::StartNew()
    do {
        $items = [GomokuSmokeWindows]::ListText($roomList.Handle)
        for ($index = 0; $index -lt $items.Length; $index++) {
            if ($items[$index].EndsWith(" $RoomId", [StringComparison]::Ordinal)) {
                [GomokuSmokeWindows]::SelectIndex($roomList.Handle, $index, $false)
                Invoke-GomokuButton $Window '加入房间'
                return
            }
        }
        Start-Sleep -Milliseconds 100
    } while ($timer.Elapsed.TotalSeconds -lt 10)
    throw "The test room $RoomId was not discovered."
}

function Start-GomokuDuel {
    param([IntPtr] $Owner, [IntPtr] $Player, [string] $RoomId)

    Join-GomokuRoom $Player $RoomId
    Wait-GomokuControl $Player -Text '你在观众席' | Out-Null
    Invoke-GomokuButton $Player '选择白席'
    Wait-GomokuControl $Player -Text '你执白' | Out-Null
    Invoke-GomokuButton $Owner '开始对局'
    Wait-GomokuControl $Owner -Text '轮到你落子' | Out-Null
}

function Get-GomokuBoard {
    param([IntPtr] $Window)

    $board = [GomokuSmokeWindows]::Children($Window) | Where-Object {
        if ($_.Visible -and $_.ClassName -like 'WindowsForms10.Window.*' -and
            [GomokuSmokeWindows]::Children($_.Handle).Length -eq 0) {
            $width, $height = [GomokuSmokeWindows]::ClientSize($_.Handle)
            $width -ge 200 -and $height -ge 180
        }
    } | Select-Object -First 1
    if (!$board) { throw 'The test board was not found.' }
    return $board.Handle
}

function Invoke-GomokuIntersection {
    param([IntPtr] $Window, [int] $Row, [int] $Column)

    $board = Get-GomokuBoard $Window
    $width, $height = [GomokuSmokeWindows]::ClientSize($board)
    $spacing = ([Math]::Min($width, $height) - 8) * 0.89 / 14
    $horizontal = [Math]::Round($width / 2 + ($Column - 7) * $spacing)
    $vertical = [Math]::Round($height / 2 + ($Row - 7) * $spacing)
    [GomokuSmokeWindows]::Click($board, $horizontal, $vertical)
}

function Get-GomokuWindowBitmap {
    param([IntPtr] $Window)

    $bounds = [GomokuSmokeWindows]::Bounds($Window)
    $bitmap = [Drawing.Bitmap]::new($bounds[2], $bounds[3])
    try {
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        try {
            $context = $graphics.GetHdc()
            try {
                if (![GomokuSmokeWindows]::PrintWindow($Window, $context, 2)) {
                    throw 'The test window could not be captured.'
                }
            } finally {
                $graphics.ReleaseHdc($context)
            }
        } finally {
            $graphics.Dispose()
        }
        return $bitmap
    } catch {
        $bitmap.Dispose()
        throw
    }
}

function Assert-GomokuDanmaku {
    param([IntPtr] $Owner, [IntPtr] $Viewer)

    $previousDpi = [GomokuSmokeWindows]::SetThreadDpiAwarenessContext([IntPtr]::new(-4))
    try {
        $windowBounds = [GomokuSmokeWindows]::Bounds($Owner)
        $boardLeft, $boardTop, $boardWidth, $boardHeight = [GomokuSmokeWindows]::Bounds((Get-GomokuBoard $Owner))
        $side = [Math]::Min($boardWidth, $boardHeight)
        $left = [int] ($boardLeft - $windowBounds[0] + ($boardWidth - $side) / 2)
        $top = [int] ($boardTop - $windowBounds[1] + ($boardHeight - $side) / 2)
        $before = Get-GomokuWindowBitmap $Owner
        try {
            $edit = Wait-GomokuControl $Viewer -ClassName 'WindowsForms10.EDIT.*' -Enabled
            [GomokuSmokeWindows]::SetText($edit.Handle, '回来观战')
            Invoke-GomokuButton $Viewer '发送弹幕'
            $timer = [Diagnostics.Stopwatch]::StartNew()
            do {
                Start-Sleep -Milliseconds 180
                $after = Get-GomokuWindowBitmap $Owner
                try {
                    $changed = 0
                    for ($row = $top; $row -lt $top + $side / 4; $row += 3) {
                        for ($column = $left; $column -lt $left + $side; $column += 3) {
                            if ($before.GetPixel($column, $row) -ne $after.GetPixel($column, $row)) { $changed++ }
                            if ($changed -ge 40) { return }
                        }
                    }
                } finally {
                    $after.Dispose()
                }
            } while ($timer.Elapsed.TotalSeconds -lt 3)
            throw 'Danmaku from the returning spectator did not appear on the host board.'
        } finally {
            $before.Dispose()
        }
    } finally {
        [GomokuSmokeWindows]::SetThreadDpiAwarenessContext($previousDpi) | Out-Null
    }
}
