namespace Gomoku.Core.Lan;

public sealed partial class LanRoomAuthority
{
    public const int MinReconnectWaitSeconds = 15;
    public const int MaxReconnectWaitSeconds = 180;
    public const int DefaultReconnectWaitSeconds = 30;
    internal const int RejoinTokenLength = 64;
    private readonly int? reconnectWaitSeconds;
    private readonly TimeProvider timeProvider;
    private LocalConnection? disconnected;
    private long disconnectedAt;
    private ITimer? reconnectTimer;
    private string? expiredRejoinToken;
    private LanMatchAbortReason? abortReason;

    internal void LeaveGuest(string roomId, string rejoinToken)
    {
        lock (gate)
        {
            if (closed || roomId != RoomId)
            {
                throw new LanRoomException(LanRoomError.RoomNotFound);
            }

            ExpireReconnectWait();
            if (disconnected is { } guest && guest.RejoinToken == rejoinToken)
            {
                Abort(LanMatchAbortReason.PlayerLeft);
                Publish();
            }
            else if (members.FirstOrDefault(member => member != host && member.RejoinToken == rejoinToken) is { } active)
            {
                Leave(active, voluntary: true);
            }
            else
            {
                throw new LanRoomException(LanRoomError.RejoinUnavailable);
            }
        }
    }

    private ILanRoomGuestConnection Rejoin(string token)
    {
        if (disconnected is not { } guest || guest.RejoinToken != token)
        {
            var error = token == expiredRejoinToken ? LanRoomError.RejoinExpired :
                phase is LanRoomPhase.Playing or LanRoomPhase.Paused && members.Any(member =>
                    member != host && member.RejoinToken == token && SeatOf(member) is not null)
                    ? LanRoomError.RejoinPending : LanRoomError.RejoinUnavailable;
            throw new LanRoomException(error);
        }

        var restored = new LocalConnection(this, guest.RejoinToken);
        if (black == guest)
        {
            black = restored;
        }
        else
        {
            white = restored;
        }

        EndReconnectWait();
        phase = LanRoomPhase.Playing;
        members.Add(restored);
        Publish();
        if (closed)
        {
            throw new LanRoomException(LanRoomError.RoomNotFound);
        }

        return restored;
    }

    private void BeginReconnectWait(LocalConnection guest)
    {
        phase = LanRoomPhase.Paused;
        matchRequest = null;
        disconnected = guest;
        disconnectedAt = timeProvider.GetTimestamp();
        var second = TimeSpan.FromSeconds(1);
        reconnectTimer = timeProvider.CreateTimer(_ => PublishReconnectCountdown(), null, second, second);
    }

    private int SecondsRemaining() => Math.Clamp((int)Math.Ceiling(
        reconnectWaitSeconds!.Value - timeProvider.GetElapsedTime(disconnectedAt).TotalSeconds),
        1, reconnectWaitSeconds.Value);

    private void PublishReconnectCountdown()
    {
        lock (gate)
        {
            if (disconnected is not null && !ExpireReconnectWait())
            {
                Publish();
            }
        }
    }

    private bool ExpireReconnectWait()
    {
        if (disconnected is not { } guest ||
            timeProvider.GetElapsedTime(disconnectedAt).TotalSeconds < reconnectWaitSeconds!.Value)
        {
            return false;
        }

        expiredRejoinToken = guest.RejoinToken;
        Abort(LanMatchAbortReason.ReconnectTimedOut);
        Publish();
        return true;
    }

    private void Abort(LanMatchAbortReason reason)
    {
        if (disconnected is { } guest)
        {
            ClearSeat(guest);
        }

        EndReconnectWait();
        abortReason = reason;
        phase = LanRoomPhase.Aborted;
    }

    private void EndReconnectWait()
    {
        disconnected = null;
        reconnectTimer?.Dispose();
        reconnectTimer = null;
    }
}
