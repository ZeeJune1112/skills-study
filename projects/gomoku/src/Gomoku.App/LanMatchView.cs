using Gomoku.Core;
using Gomoku.Core.Lan;

namespace Gomoku.App;

internal sealed partial class LanMatchView : UserControl
{
    private ILanRoomConnection connection;
    private readonly TcpLanRoomHost? host;
    private readonly CancellationTokenSource lifetime = new();
    private readonly BoardControl board = new() { Dock = DockStyle.Fill, TabIndex = 0 };
    private readonly Button black = GameStyle.ActionButton("选择黑席", compact: true);
    private readonly Button white = GameStyle.ActionButton("选择白席", compact: true);
    private readonly Button withdraw = GameStyle.ActionButton("退回观众席", compact: true);
    private readonly Button start = GameStyle.ActionButton("开始对局", primary: true, compact: true);
    private readonly Label difficultyLabel = new() { AutoSize = true };
    private readonly AiDifficultyPicker difficultyPicker = new() { Width = 156 };
    private readonly Button undoAi = GameStyle.ActionButton("悔棋", compact: true);
    private readonly Button restartAi = GameStyle.ActionButton("重开", primary: true, compact: true);
    private readonly Button exit = GameStyle.ActionButton("退出房间", compact: true);
    private LanRoomSnapshot? snapshot;
    private string? broadcastError;
    private bool connected = true;
    private bool pending;
    private bool disposed;

    public event Action<string?>? ExitRequested;

    public LanMatchView(ILanRoomConnection connection, TcpLanRoomHost? host)
    {
        this.connection = connection;
        this.host = host;
        Controls.Add(CreateRoomLayout());

        black.Click += (_, _) => Send(new LanRoomCommand.SelectSeat(Stone.Black));
        white.Click += (_, _) => Send(new LanRoomCommand.SelectSeat(Stone.White));
        withdraw.Click += (_, _) => Send(new LanRoomCommand.WithdrawSeat());
        start.Click += (_, _) => Send(new LanRoomCommand.StartMatch());
        undoAi.Click += (_, _) => Send(new LanRoomCommand.UndoAiMatch());
        restartAi.Click += (_, _) => Send(new LanRoomCommand.RestartAiMatch());
        difficultyPicker.SelectionChangeCommitted += (_, _) =>
        {
            if (snapshot is { IsHost: true, Phase: LanRoomPhase.Finished, AiDifficulty: not null } state &&
                state.AiDifficulty != difficultyPicker.SelectedDifficulty)
            {
                Send(new LanRoomCommand.SetAiDifficulty(difficultyPicker.SelectedDifficulty));
            }
        };
        exit.Click += async (_, _) =>
        {
            exit.Enabled = false;
            var notice = await LeaveRoomAsync();
            if (!disposed)
            {
                ExitRequested?.Invoke(notice);
            }
        };
        board.PointSelected += point => Send(new LanRoomCommand.PlaceStone(point.Row, point.Column));
        Load += StartConnection;
        Render();
    }

    public void SelectBoard()
    {
        if (board.TabStop)
        {
            board.Select();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !disposed)
        {
            disposed = true;
            StopConnection();
            lifetime.Dispose();
        }

        base.Dispose(disposing);
        if (disposing)
        {
            roomTitleFont.Dispose();
            roleTitleFont.Dispose();
        }
    }

    private void StartConnection(object? sender, EventArgs args)
    {
        Load -= StartConnection;
        var cancellationToken = lifetime.Token;
        _ = ReceiveAsync(cancellationToken);
        if (host is not null && !cancellationToken.IsCancellationRequested)
        {
            _ = AdvertiseAsync(host, cancellationToken);
        }
    }

    private async Task ReceiveAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var active = connection;
            var failureMessage = "房间连接已结束。";
            try
            {
                await foreach (var message in active.ReadEventsAsync(cancellationToken))
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }

                    if (message is LanRoomEvent.Closed closed)
                    {
                        if (leavingRoom)
                        {
                            leaveAcknowledged?.TrySetResult(true);
                        }
                        else
                        {
                            ExitRequested?.Invoke(closed.Reason == LanRoomCloseReason.HostClosed
                                ? "房主已关闭房间。"
                                : null);
                        }
                        return;
                    }

                    if (ReceiveDanmaku(message))
                    {
                        continue;
                    }

                    pending = false;
                    if (message is LanRoomEvent.State state)
                    {
                        snapshot = state.Snapshot;
                    }
                    else if (message is LanRoomEvent.Rejected rejected)
                    {
                        error.Text = LanRoomText.Describe(rejected.Error);
                    }

                    Render();
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception failure) when (LanRoomText.IsNetworkFailure(failure))
            {
                failureMessage = LanRoomText.Describe(failure, "房间连接已中断。");
            }

            if (leavingRoom || cancellationToken.IsCancellationRequested)
            {
                leaveAcknowledged?.TrySetResult(false);
                return;
            }

            if (active is TcpLanRoomConnection guest && snapshot is
                { Seat: not null, ReconnectWaitSeconds: not null, Phase: LanRoomPhase.Playing or LanRoomPhase.Paused })
            {
                if (await ReconnectAsync(guest, cancellationToken))
                {
                    continue;
                }
            }
            else
            {
                Disconnect(failureMessage);
            }

            return;
        }
    }

    private async Task AdvertiseAsync(TcpLanRoomHost room, CancellationToken cancellationToken)
    {
        try
        {
            await LanDiscovery.AdvertiseAsync(room.RoomId, room.Port, cancellationToken, isAi: room.IsAi);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception failure) when (LanRoomText.IsNetworkFailure(failure))
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                error.Text = broadcastError = "无法广播房间，请检查网络和防火墙。";
            }
        }
    }

    private async void Send(LanRoomCommand command)
    {
        if (!connected || pending || leavingRoom)
        {
            return;
        }

        var cancellationToken = lifetime.Token;
        var active = connection;
        error.Text = broadcastError ?? "";
        if (command is not LanRoomCommand.SendDanmaku)
        {
            pending = true;
            Render();
        }
        try
        {
            await active.SendAsync(command, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception failure) when (LanRoomText.IsNetworkFailure(failure))
        {
            if (!cancellationToken.IsCancellationRequested && !leavingRoom && ReferenceEquals(active, connection))
            {
                if (active is TcpLanRoomConnection)
                {
                    active.Dispose();
                }
                else
                {
                    Disconnect(LanRoomText.Describe(failure, "无法向房间发送操作。"));
                }
            }
        }
    }

    private void Disconnect(string message)
    {
        connected = false;
        error.Text = message;
        Render();
        StopConnection();
    }

    private void StopConnection()
    {
        lifetime.Cancel();
        connection.Dispose();
        if (host is not null)
        {
            _ = StopHostAsync(host);
        }
    }

    private static async Task StopHostAsync(TcpLanRoomHost room)
    {
        try
        {
            await room.DisposeAsync();
        }
        catch (Exception failure) when (LanRoomText.IsNetworkFailure(failure))
        {
        }
    }

    private void Render()
    {
        var canReassignSeats = snapshot?.Phase is LanRoomPhase.Waiting or LanRoomPhase.Aborted;
        var canAct = connected && !pending && !leavingRoom;
        black.Visible = canReassignSeats && snapshot?.BlackOccupied == false;
        white.Visible = canReassignSeats && snapshot?.WhiteOccupied == false;
        withdraw.Visible = canReassignSeats && snapshot?.Seat is not null;
        start.Visible = canReassignSeats && snapshot?.IsHost == true;
        black.Enabled = white.Enabled = withdraw.Enabled = canAct;
        start.Enabled = canAct && snapshot is { BlackOccupied: true, WhiteOccupied: true };
        var aiHost = snapshot is { IsHost: true, AiDifficulty: not null };
        difficultyLabel.Visible = difficultyPicker.Visible = undoAi.Visible = restartAi.Visible = aiHost;
        difficultyPicker.Enabled = canAct && snapshot?.Phase == LanRoomPhase.Finished;
        undoAi.Enabled = canAct && snapshot is { Phase: LanRoomPhase.Playing, Board.Moves.Count: > 0 };
        restartAi.Enabled = canAct;
        RenderRoomDetails();
        if (snapshot is { } state)
        {
            if (state.IsHost && state.AiDifficulty is { } difficulty && !pending)
            {
                difficultyLabel.Text = state.Phase == LanRoomPhase.Finished ? "下一局难度" : "人机难度";
                difficultyPicker.SelectedDifficulty = difficulty;
            }

            board.SetPosition(state.Board, state.Seat, canAct && state.Phase == LanRoomPhase.Playing);
        }

        RenderMatchRequests(canAct);
        RenderDanmakuControls(canAct);
    }

    private string PositionText() => snapshot?.Phase switch
    {
        LanRoomPhase.Waiting => "等待房主开局",
        LanRoomPhase.Paused when snapshot.ReconnectWait is { } wait =>
            $"{(wait.Seat == Stone.Black ? "黑棋" : "白棋")}离线，暂停对局。剩余 {wait.SecondsRemaining} 秒。",
        LanRoomPhase.Aborted => snapshot.AbortReason switch
        {
            LanMatchAbortReason.PlayerDisconnected => "对局已中止，棋手断线。",
            LanMatchAbortReason.ReconnectTimedOut => "对局已中止，等待重连超时。",
            LanMatchAbortReason.BothPlayersLeft => "对局已中止，双方棋手均已离开。",
            _ => "对局已中止，棋手主动退出。"
        },
        LanRoomPhase.Finished => snapshot.Board.Outcome switch
        {
            MatchOutcome.BlackWon => "黑棋获胜",
            MatchOutcome.WhiteWon => "白棋获胜",
            _ => "和棋"
        },
        LanRoomPhase.Playing => snapshot.Board.Turn switch
        {
            Stone.Black => snapshot.Seat == Stone.Black ? "轮到你落子" : "轮到黑棋",
            Stone.White when snapshot.AiDifficulty.HasValue => "电脑思考中",
            _ => snapshot.Seat == Stone.White ? "轮到你落子" : "轮到白棋"
        },
        _ => "正在连接房间…"
    };
}
