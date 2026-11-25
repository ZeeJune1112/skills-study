using Gomoku.Core.Lan;

namespace Gomoku.App;

internal sealed partial class LanMatchView
{
    private CancellationTokenSource? reconnectAttempt;
    private TaskCompletionSource<bool>? leaveAcknowledged;
    private Task<string?>? leaveTask;
    private bool leavingRoom;

    public Task<string?> LeaveRoomAsync() => leaveTask ??= LeaveCoreAsync();

    private async Task<bool> ReconnectAsync(TcpLanRoomConnection guest, CancellationToken cancellationToken)
    {
        using var attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        reconnectAttempt = attempt;
        connected = false;
        pending = false;
        error.Text = "";
        Render();
        try
        {
            var restored = await guest.RejoinAsync(attempt.Token);
            if (attempt.IsCancellationRequested)
            {
                restored.Dispose();
                return false;
            }

            connection = restored;
            connected = true;
            return true;
        }
        catch (OperationCanceledException) when (attempt.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception failure) when (LanRoomText.IsNetworkFailure(failure))
        {
            if (!attempt.IsCancellationRequested)
            {
                reconnectAttempt = null;
                Disconnect(LanRoomText.Describe(failure, "无法恢复对局。"));
            }

            return false;
        }
        finally
        {
            reconnectAttempt = null;
        }
    }

    private async Task<string?> LeaveCoreAsync()
    {
        leavingRoom = true;
        reconnectAttempt?.Cancel();
        Render();
        string? notice = null;
        if (host is not null)
        {
            await StopHostAsync(host);
        }
        else if (connection is TcpLanRoomConnection guest)
        {
            if (snapshot is { Phase: LanRoomPhase.Playing, Seat: not null, ReconnectWaitSeconds: not null } state)
            {
                if (LanRejoinTicket.Save(state.RoomId, guest))
                {
                    guest.Dispose();
                    return "已暂离对局。请在等待时间内重新加入此房间。";
                }

                notice = "无法保存续局凭据，已退出房间。";
            }

            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            if (connected)
            {
                leaveAcknowledged = new(TaskCreationOptions.RunContinuationsAsynchronously);
                try
                {
                    await guest.SendAsync(new LanRoomCommand.LeaveRoom(), deadline.Token);
                    if (await leaveAcknowledged.Task.WaitAsync(TimeSpan.FromSeconds(1), deadline.Token))
                    {
                        return notice;
                    }
                }
                catch (Exception failure) when (LanRoomText.IsNetworkFailure(failure) || failure is OperationCanceledException)
                {
                }
            }

            try
            {
                await guest.NotifyDepartureAsync(deadline.Token);
            }
            catch (Exception failure) when (LanRoomText.IsNetworkFailure(failure) || failure is OperationCanceledException)
            {
            }
        }

        return notice;
    }
}
