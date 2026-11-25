namespace Gomoku.Core.Lan;

public sealed partial class LanRoomAuthority
{
    private LanMatchRequest? matchRequest;

    private bool CanNegotiateMatchChange =>
        black is not null && white is not null && phase is LanRoomPhase.Playing or LanRoomPhase.Finished;

    private LanRoomError? RequestMatchChange(LocalConnection member, LanMatchChange change)
    {
        if (SeatOf(member) is not { } seat)
        {
            return LanRoomError.NoSeat;
        }

        if (matchRequest?.Status == LanMatchRequestStatus.Pending)
        {
            return LanRoomError.MatchRequestPending;
        }

        if (!CanNegotiateMatchChange || !IsMatchChangeAvailable(change, seat))
        {
            return LanRoomError.MatchRequestUnavailable;
        }

        matchRequest = new(Guid.NewGuid(), change, seat, LanMatchRequestStatus.Pending);
        return null;
    }

    private bool IsMatchChangeAvailable(LanMatchChange change, Stone seat) => change switch
    {
        LanMatchChange.Undo => phase == LanRoomPhase.Playing && match.Snapshot.Moves.Any(move => move.Stone == seat),
        LanMatchChange.Restart => true,
        _ => false
    };

    private LanRoomError? RespondToMatchRequest(LocalConnection member, LanRoomCommand.RespondToMatchRequest response)
    {
        if (SeatOf(member) is not { } seat)
        {
            return LanRoomError.NoSeat;
        }

        if (matchRequest is not { Status: LanMatchRequestStatus.Pending } request || request.Id != response.RequestId)
        {
            return LanRoomError.MatchRequestExpired;
        }

        if (seat == request.Requester)
        {
            return LanRoomError.MatchRequestOpponentOnly;
        }

        if (response.Confirm)
        {
            if (request.Change == LanMatchChange.Restart)
            {
                match.Restart();
                phase = LanRoomPhase.Playing;
            }
            else if (!match.TryUndo(request.Requester))
            {
                return LanRoomError.MatchRequestUnavailable;
            }
        }

        matchRequest = request with
        {
            Status = response.Confirm ? LanMatchRequestStatus.Confirmed : LanMatchRequestStatus.Refused
        };
        return null;
    }
}
