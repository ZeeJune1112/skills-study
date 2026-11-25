using System.Security.Cryptography;
using System.Threading.Channels;

namespace Gomoku.Core.Lan;

public sealed partial class LanRoomAuthority : IDisposable
{
    public const int DefaultSpectatorCapacity = 5;
    public const int MinSpectatorCapacity = 1;
    public const int MaxSpectatorCapacity = 30;
    internal const int RoomIdLength = 16;
    private readonly object gate = new();
    private readonly RoomSession match;
    private readonly HashSet<LocalConnection> members = [];
    private readonly LocalConnection host;
    private readonly int spectatorCapacity;
    private LocalConnection? black;
    private LocalConnection? white;
    private LanRoomPhase phase;
    private bool closed;

    public string RoomId { get; } = RandomNumberGenerator.GetHexString(RoomIdLength);
    public ILanRoomConnection Host => host;
    private int SpectatorCount => members.Count(member => SeatOf(member) is null);
    private bool CanReassignSeats => phase is LanRoomPhase.Waiting or LanRoomPhase.Aborted;

    public LanRoomAuthority(int spectatorCapacity = DefaultSpectatorCapacity, AiDifficulty? aiDifficulty = null,
        int? reconnectWaitSeconds = null, TimeProvider? timeProvider = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(spectatorCapacity, MinSpectatorCapacity);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(spectatorCapacity, MaxSpectatorCapacity);
        if (reconnectWaitSeconds is { } seconds)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(seconds, MinReconnectWaitSeconds);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(seconds, MaxReconnectWaitSeconds);
        }

        this.spectatorCapacity = spectatorCapacity;
        this.reconnectWaitSeconds = reconnectWaitSeconds;
        this.timeProvider = timeProvider ?? TimeProvider.System;
        IsAi = aiDifficulty.HasValue;
        match = new(aiDifficulty ?? AiDifficulty.Easy);
        phase = IsAi ? LanRoomPhase.Playing : LanRoomPhase.Waiting;
        black = host = new(this, "");
        members.Add(host);
        host.Post(SnapshotFor(host));
    }

    public ILanRoomGuestConnection Join(string roomId, string? rejoinToken = null)
    {
        lock (gate)
        {
            if (closed || roomId != RoomId)
            {
                throw new LanRoomException(LanRoomError.RoomNotFound);
            }

            ExpireReconnectWait();
            if (rejoinToken is not null)
            {
                return Rejoin(rejoinToken);
            }

            if (SpectatorCount >= spectatorCapacity)
            {
                throw new LanRoomException(LanRoomError.RoomFull);
            }

            var guest = new LocalConnection(this, RandomNumberGenerator.GetHexString(RejoinTokenLength));
            members.Add(guest);
            Publish();
            if (closed)
            {
                throw new LanRoomException(LanRoomError.RoomNotFound);
            }

            return guest;
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            Close();
        }
    }

    internal static bool IsRoomId(string? value) =>
        value is { Length: RoomIdLength } && value.All(char.IsAsciiHexDigitUpper);

    private void Submit(LocalConnection member, LanRoomCommand command)
    {
        lock (gate)
        {
            if (!members.Contains(member))
            {
                throw new LanRoomException(LanRoomError.ConnectionClosed);
            }

            ExpireReconnectWait();
            if (command is LanRoomCommand.LeaveRoom)
            {
                Leave(member, voluntary: true);
                return;
            }

            if (command is LanRoomCommand.Heartbeat)
            {
                if (!member.Post(new LanRoomEvent.Heartbeat()))
                {
                    Leave(member, voluntary: false);
                }

                return;
            }

            if (command is LanRoomCommand.SendDanmaku danmaku)
            {
                SendDanmaku(member, danmaku);
                return;
            }

            var error = command switch
            {
                LanRoomCommand.SelectSeat select => SelectSeat(member, select.Seat),
                LanRoomCommand.WithdrawSeat => WithdrawSeat(member),
                LanRoomCommand.StartMatch => StartMatch(member),
                LanRoomCommand.PlaceStone place => PlaceStone(member, new(place.Row, place.Column)),
                LanRoomCommand.UndoAiMatch or LanRoomCommand.RestartAiMatch or LanRoomCommand.SetAiDifficulty =>
                    ApplyAiAction(member, command),
                LanRoomCommand.RequestMatchChange request => RequestMatchChange(member, request.Change),
                LanRoomCommand.RespondToMatchRequest response => RespondToMatchRequest(member, response),
                _ => throw new ArgumentOutOfRangeException(nameof(command))
            };

            if (error is null)
            {
                Publish();
            }
            else if (!member.Post(new LanRoomEvent.Rejected(error.Value)))
            {
                Leave(member, voluntary: false);
            }
        }
    }

    private LanRoomError? SelectSeat(LocalConnection member, Stone seat)
    {
        if (!CanReassignSeats)
        {
            return LanRoomError.SeatsLocked;
        }

        if (seat is not (Stone.Black or Stone.White))
        {
            return LanRoomError.InvalidSeat;
        }

        if ((seat == Stone.Black ? black : white) is not null)
        {
            return LanRoomError.SeatTaken;
        }

        ClearSeat(member);
        if (seat == Stone.Black)
        {
            black = member;
        }
        else
        {
            white = member;
        }

        return null;
    }

    private LanRoomError? WithdrawSeat(LocalConnection member)
    {
        if (!CanReassignSeats)
        {
            return LanRoomError.SeatsLocked;
        }

        if (SeatOf(member) is null)
        {
            return LanRoomError.NoSeat;
        }

        if (SpectatorCount >= spectatorCapacity)
        {
            return LanRoomError.RoomFull;
        }

        ClearSeat(member);
        return null;
    }

    private LanRoomError? StartMatch(LocalConnection member)
    {
        if (member != host)
        {
            return LanRoomError.HostOnly;
        }

        if (!CanReassignSeats)
        {
            return LanRoomError.SeatsLocked;
        }

        if (black is null || white is null)
        {
            return LanRoomError.SeatsUnfilled;
        }

        if (phase == LanRoomPhase.Aborted)
        {
            match.Restart();
            abortReason = null;
        }

        phase = LanRoomPhase.Playing;
        return null;
    }

    private LanRoomError? PlaceStone(LocalConnection member, BoardPoint point)
    {
        if (phase != LanRoomPhase.Playing)
        {
            return LanRoomError.MatchNotPlaying;
        }

        if (SeatOf(member) is not { } seat)
        {
            return LanRoomError.ObserverCannotPlay;
        }

        if (match.Snapshot.Turn != seat)
        {
            return LanRoomError.NotYourTurn;
        }

        if (!match.TryPlace(seat, point))
        {
            return LanRoomError.IllegalMove;
        }

        matchRequest = null;
        if (match.Snapshot.Outcome != MatchOutcome.InProgress)
        {
            phase = LanRoomPhase.Finished;
        }
        else if (IsAi && match.Snapshot.Turn == Stone.White)
        {
            StartAiTurn();
        }

        return null;
    }

    private Stone? SeatOf(LocalConnection member) =>
        member == black ? Stone.Black : member == white ? Stone.White : null;

    private LanRoomEvent.State SnapshotFor(LocalConnection member)
    {
        var board = phase is LanRoomPhase.Waiting or LanRoomPhase.Aborted
            ? match.Snapshot with { Turn = null }
            : match.Snapshot;
        var seat = SeatOf(member);
        return new(new LanRoomSnapshot(RoomId, member == host, seat, black is not null, white is not null || IsAi,
            phase, board, SpectatorCount, spectatorCapacity, IsAi ? match.Difficulty : null)
        {
            ReconnectWaitSeconds = reconnectWaitSeconds,
            ReconnectWait = disconnected is { } guest ? new(SeatOf(guest)!.Value, SecondsRemaining()) : null,
            AbortReason = abortReason,
            CanRequestMatchChange = seat is not null && CanNegotiateMatchChange,
            MatchRequest = matchRequest
        });
    }

    private void Publish()
    {
        bool membersChanged;
        do
        {
            membersChanged = false;
            foreach (var member in members.ToArray())
            {
                if (!member.Post(SnapshotFor(member)))
                {
                    if (member == host)
                    {
                        Close();
                        return;
                    }

                    membersChanged |= Remove(member, voluntary: false);
                }
            }
        } while (membersChanged);
    }

    private void Leave(LocalConnection member, bool voluntary)
    {
        lock (gate)
        {
            ExpireReconnectWait();
            if (member == host)
            {
                Close();
            }
            else if (Remove(member, voluntary))
            {
                Publish();
            }
        }
    }

    private bool Remove(LocalConnection member, bool voluntary)
    {
        if (!members.Remove(member))
        {
            return false;
        }

        member.Close(voluntary ? LanRoomCloseReason.Left : null);
        if (SeatOf(member) is null)
        {
            return true;
        }

        if (!voluntary && phase == LanRoomPhase.Playing && reconnectWaitSeconds.HasValue)
        {
            BeginReconnectWait(member);
            return true;
        }

        ClearSeat(member);
        if (phase == LanRoomPhase.Paused)
        {
            Abort(LanMatchAbortReason.BothPlayersLeft);
        }
        else if (phase == LanRoomPhase.Playing)
        {
            Abort(voluntary ? LanMatchAbortReason.PlayerLeft : LanMatchAbortReason.PlayerDisconnected);
        }

        return true;
    }

    private void ClearSeat(LocalConnection member)
    {
        matchRequest = null;
        if (black == member)
        {
            black = null;
        }

        if (white == member)
        {
            white = null;
        }
    }

    private void Close()
    {
        if (closed)
        {
            return;
        }

        closed = true;
        CancelAiTurn();
        EndReconnectWait();
        matchRequest = null;
        foreach (var member in members)
        {
            member.Close(LanRoomCloseReason.HostClosed);
        }

        members.Clear();
    }

    private sealed partial class LocalConnection(LanRoomAuthority room, string rejoinToken) : ILanRoomGuestConnection
    {
        private readonly Channel<LanRoomEvent> events = Channel.CreateBounded<LanRoomEvent>(
            new BoundedChannelOptions(64) { SingleReader = true, SingleWriter = true });

        public string RejoinToken { get; } = rejoinToken;

        public ValueTask SendAsync(LanRoomCommand command, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            room.Submit(this, command);
            return ValueTask.CompletedTask;
        }

        public IAsyncEnumerable<LanRoomEvent> ReadEventsAsync(CancellationToken cancellationToken = default) =>
            events.Reader.ReadAllAsync(cancellationToken);

        public bool Post(LanRoomEvent message) => events.Writer.TryWrite(message);
        public void Close(LanRoomCloseReason? reason)
        {
            if (reason is { } closure)
            {
                Post(new LanRoomEvent.Closed(closure));
            }

            events.Writer.TryComplete();
        }

        public void Disconnect() => room.Leave(this, voluntary: false);
        public void Dispose() => room.Leave(this, voluntary: true);
    }
}
