namespace Gomoku.Core.Lan;

public enum LanMatchChange
{
    Undo,
    Restart
}

public enum LanMatchRequestStatus
{
    Pending,
    Confirmed,
    Refused
}

public sealed record LanMatchRequest(Guid Id, LanMatchChange Change, Stone Requester, LanMatchRequestStatus Status);
