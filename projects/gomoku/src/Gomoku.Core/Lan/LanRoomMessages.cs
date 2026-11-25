using System.Text.Json.Serialization;

namespace Gomoku.Core.Lan;

public interface ILanRoomConnection : IDisposable
{
    ValueTask SendAsync(LanRoomCommand command, CancellationToken cancellationToken = default);
    IAsyncEnumerable<LanRoomEvent> ReadEventsAsync(CancellationToken cancellationToken = default);
}

public interface ILanRoomGuestConnection : ILanRoomConnection
{
    string RejoinToken { get; }
    void Disconnect();
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(LanRoomCommand.Heartbeat), "heartbeat")]
[JsonDerivedType(typeof(LanRoomCommand.LeaveRoom), "leaveRoom")]
[JsonDerivedType(typeof(LanRoomCommand.SelectSeat), "selectSeat")]
[JsonDerivedType(typeof(LanRoomCommand.WithdrawSeat), "withdrawSeat")]
[JsonDerivedType(typeof(LanRoomCommand.StartMatch), "startMatch")]
[JsonDerivedType(typeof(LanRoomCommand.PlaceStone), "placeStone")]
[JsonDerivedType(typeof(LanRoomCommand.UndoAiMatch), "undoAiMatch")]
[JsonDerivedType(typeof(LanRoomCommand.RestartAiMatch), "restartAiMatch")]
[JsonDerivedType(typeof(LanRoomCommand.SetAiDifficulty), "setAiDifficulty")]
[JsonDerivedType(typeof(LanRoomCommand.RequestMatchChange), "requestMatchChange")]
[JsonDerivedType(typeof(LanRoomCommand.RespondToMatchRequest), "respondToMatchRequest")]
public abstract partial record LanRoomCommand
{
    private LanRoomCommand() { }

    internal sealed record Heartbeat : LanRoomCommand;
    public sealed record LeaveRoom : LanRoomCommand;
    public sealed record SelectSeat(Stone Seat) : LanRoomCommand;
    public sealed record WithdrawSeat : LanRoomCommand;
    public sealed record StartMatch : LanRoomCommand;
    public sealed record PlaceStone(int Row, int Column) : LanRoomCommand;
    public sealed record UndoAiMatch : LanRoomCommand;
    public sealed record RestartAiMatch : LanRoomCommand;
    public sealed record SetAiDifficulty(AiDifficulty Difficulty) : LanRoomCommand;
    public sealed record RequestMatchChange(LanMatchChange Change) : LanRoomCommand;
    public sealed record RespondToMatchRequest(Guid RequestId, bool Confirm) : LanRoomCommand;
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(LanRoomEvent.Joined), "joined")]
[JsonDerivedType(typeof(LanRoomEvent.Heartbeat), "heartbeat")]
[JsonDerivedType(typeof(LanRoomEvent.Closed), "closed")]
[JsonDerivedType(typeof(LanRoomEvent.State), "state")]
[JsonDerivedType(typeof(LanRoomEvent.Rejected), "rejected")]
public abstract partial record LanRoomEvent
{
    private LanRoomEvent() { }

    internal sealed record Joined(string? RejoinToken) : LanRoomEvent;
    internal sealed record Heartbeat : LanRoomEvent;
    public sealed record Closed(LanRoomCloseReason Reason) : LanRoomEvent;
    public sealed record State(LanRoomSnapshot Snapshot) : LanRoomEvent;
    public sealed record Rejected(LanRoomError Error) : LanRoomEvent;
}

public sealed record LanRoomSnapshot(string RoomId, bool IsHost, Stone? Seat, bool BlackOccupied,
    bool WhiteOccupied, LanRoomPhase Phase, RoomSnapshot Board, int SpectatorCount, int SpectatorCapacity,
    AiDifficulty? AiDifficulty)
{
    public int? ReconnectWaitSeconds { get; init; }
    public LanReconnectWait? ReconnectWait { get; init; }
    public LanMatchAbortReason? AbortReason { get; init; }
    public bool CanRequestMatchChange { get; init; }
    public LanMatchRequest? MatchRequest { get; init; }
}

public sealed record LanReconnectWait(Stone Seat, int SecondsRemaining);

public enum LanMatchAbortReason
{
    PlayerDisconnected,
    ReconnectTimedOut,
    PlayerLeft,
    BothPlayersLeft
}

public enum LanRoomCloseReason
{
    Left,
    HostClosed
}

public enum LanRoomPhase
{
    Waiting,
    Playing,
    Finished,
    Aborted,
    Paused
}

public enum LanRoomError
{
    RoomNotFound,
    RoomFull,
    ConnectionClosed,
    IncompatibleVersion,
    InvalidSeat,
    SeatTaken,
    NoSeat,
    SeatsLocked,
    HostOnly,
    SeatsUnfilled,
    MatchNotPlaying,
    ObserverCannotPlay,
    NotYourTurn,
    IllegalMove,
    MatchRequestUnavailable,
    MatchRequestPending,
    MatchRequestExpired,
    MatchRequestOpponentOnly,
    AiActionUnavailable,
    DanmakuSpectatorOnly,
    InvalidDanmaku,
    RejoinPending,
    RejoinUnavailable,
    RejoinExpired
}

public sealed class LanRoomException(LanRoomError error) : IOException(error.ToString())
{
    public LanRoomError Error { get; } = error;
}
