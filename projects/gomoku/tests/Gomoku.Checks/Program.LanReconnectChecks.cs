using Gomoku.Core;
using Gomoku.Core.Lan;
using System.Text.Json;

namespace Gomoku.Checks;

internal static partial class Program
{
    [Check]
    private static void LanRoomPausesAndRestoresTheReservedSeatWithAFullAudience() =>
        CheckLanRoomRejoinAsync().GetAwaiter().GetResult();

    private static async Task CheckLanRoomRejoinAsync()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var clock = new ReconnectClock();
        using var room = new LanRoomAuthority(spectatorCapacity: 1,
            reconnectWaitSeconds: LanRoomAuthority.DefaultReconnectWaitSeconds, timeProvider: clock);
        var host = room.Host;
        using var guest = room.Join(room.RoomId);
        await using var hostEvents = host.ReadEventsAsync(deadline.Token).GetAsyncEnumerator();
        await guest.SendAsync(new LanRoomCommand.SelectSeat(Stone.White));
        await host.SendAsync(new LanRoomCommand.StartMatch());
        await host.SendAsync(new LanRoomCommand.PlaceStone(7, 7));
        using var observer = room.Join(room.RoomId);
        await using var observerEvents = observer.ReadEventsAsync(deadline.Token).GetAsyncEnumerator();
        var playing = await ExpectRoomAsync(hostEvents, state => state.SpectatorCount == 1 && state.Board.Moves.Count == 1);
        Require(playing.ReconnectWaitSeconds == 30, "Waiting defaults to 30 seconds.");
        Require(guest.RejoinToken.Length >= 32 && guest.RejoinToken != observer.RejoinToken &&
            !JsonSerializer.Serialize(playing).Contains(guest.RejoinToken, StringComparison.Ordinal),
            "Every guest has a private capability that is absent from published room state.");

        guest.Disconnect();
        var paused = await ExpectRoomAsync(hostEvents, state => state.Phase == LanRoomPhase.Paused);
        Require(paused is { WhiteOccupied: true, SpectatorCount: 1, ReconnectWait.Seat: Stone.White,
            ReconnectWait.SecondsRemaining: 30, Board.Turn: Stone.White } &&
            paused.Board.Moves.SequenceEqual(playing.Board.Moves), "A paused match preserves its turn, board and seats.");
        await ExpectRoomAsync(observerEvents, state => state.Phase == LanRoomPhase.Paused);
        await ExpectRejectedAsync(host, hostEvents, new LanRoomCommand.PlaceStone(7, 8), LanRoomError.MatchNotPlaying);
        await ExpectRejectedAsync(host, hostEvents, new LanRoomCommand.RequestMatchChange(LanMatchChange.Restart),
            LanRoomError.MatchRequestUnavailable);
        clock.Advance(TimeSpan.FromSeconds(29));
        await ExpectRoomAsync(hostEvents, state => state.ReconnectWait?.SecondsRemaining == 1);
        try
        {
            using var impostor = room.Join(room.RoomId, observer.RejoinToken);
            throw new InvalidOperationException("An observer reclaimed the reserved player seat.");
        }
        catch (LanRoomException error)
        {
            Require(error.Error == LanRoomError.RejoinUnavailable, "A different participant cannot reclaim the seat.");
        }

        using var restored = room.Join(room.RoomId, guest.RejoinToken);
        await using var restoredEvents = restored.ReadEventsAsync(deadline.Token).GetAsyncEnumerator();
        var resumed = await ExpectRoomAsync(restoredEvents, _ => true);
        Require(restored.RejoinToken == guest.RejoinToken && resumed is { Seat: Stone.White, SpectatorCount: 1,
            Phase: LanRoomPhase.Playing, ReconnectWait: null, Board.Turn: Stone.White } &&
            resumed.Board.Moves.SequenceEqual(playing.Board.Moves), "The original guest returns directly to White.");
        clock.Advance(TimeSpan.FromSeconds(1));
        await restored.SendAsync(new LanRoomCommand.PlaceStone(7, 8));
        await ExpectRoomAsync(observerEvents, state => state is
            { Phase: LanRoomPhase.Playing, Board: { Moves.Count: 2, Turn: Stone.Black } });
    }

    [Check]
    private static void LanRoomExpiresAtBothConfiguredBoundaries() =>
        CheckLanRoomBoundariesAsync().GetAwaiter().GetResult();

    private static async Task CheckLanRoomBoundariesAsync()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        foreach (var invalid in new[] { 14, 181 })
        {
            try
            {
                using var room = new LanRoomAuthority(reconnectWaitSeconds: invalid);
                throw new InvalidOperationException($"A {invalid}-second wait was accepted.");
            }
            catch (ArgumentOutOfRangeException)
            {
            }
        }

        foreach (var seconds in new[] { 15, 180 })
        {
            var clock = new ReconnectClock();
            using var room = new LanRoomAuthority(reconnectWaitSeconds: seconds, timeProvider: clock);
            var host = room.Host;
            using var guest = room.Join(room.RoomId);
            await using var hostEvents = host.ReadEventsAsync(deadline.Token).GetAsyncEnumerator();
            await guest.SendAsync(new LanRoomCommand.SelectSeat(Stone.White));
            await host.SendAsync(new LanRoomCommand.StartMatch());
            await host.SendAsync(new LanRoomCommand.PlaceStone(7, 7));
            guest.Disconnect();
            await ExpectRoomAsync(hostEvents, state => state.ReconnectWait?.SecondsRemaining == seconds);
            clock.Advance(TimeSpan.FromSeconds(seconds) - TimeSpan.FromTicks(1));
            await ExpectRoomAsync(hostEvents, state => state.ReconnectWait?.SecondsRemaining == 1);
            clock.Advance(TimeSpan.FromTicks(1), publishTick: seconds == 15);
            try
            {
                using var late = room.Join(room.RoomId, guest.RejoinToken);
                throw new InvalidOperationException($"The guest rejoined at the {seconds}-second deadline.");
            }
            catch (LanRoomException error)
            {
                Require(error.Error == LanRoomError.RejoinExpired, "The deadline is exclusive even before the timer runs.");
            }

            await ExpectRoomAsync(hostEvents, state => state is
                { Phase: LanRoomPhase.Aborted, WhiteOccupied: false, ReconnectWait: null,
                    AbortReason: LanMatchAbortReason.ReconnectTimedOut,
                    Board: { Moves.Count: 1, Turn: null, Outcome: MatchOutcome.InProgress } });
        }
    }

    [Check]
    private static void LanRoomIgnoresObserverLossAndAbortsWhenBothPlayersLeave() =>
        CheckLanRoomParticipantLossAsync().GetAwaiter().GetResult();

    private static async Task CheckLanRoomParticipantLossAsync()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var room = new LanRoomAuthority(reconnectWaitSeconds: 30);
        var host = room.Host;
        await using var hostEvents = host.ReadEventsAsync(deadline.Token).GetAsyncEnumerator();
        await host.SendAsync(new LanRoomCommand.WithdrawSeat());
        using var black = room.Join(room.RoomId);
        await black.SendAsync(new LanRoomCommand.SelectSeat(Stone.Black));
        using var white = room.Join(room.RoomId);
        await white.SendAsync(new LanRoomCommand.SelectSeat(Stone.White));
        using var observer = room.Join(room.RoomId);
        await host.SendAsync(new LanRoomCommand.StartMatch());
        observer.Disconnect();
        await ExpectRoomAsync(hostEvents, state => state is
            { Phase: LanRoomPhase.Playing, SpectatorCount: 1, ReconnectWait: null });
        black.Disconnect();
        await ExpectRoomAsync(hostEvents, state => state is
            { Phase: LanRoomPhase.Paused, Seat: null, ReconnectWait.Seat: Stone.Black });
        await white.SendAsync(new LanRoomCommand.LeaveRoom());
        await ExpectRoomAsync(hostEvents, state => state is
            { Phase: LanRoomPhase.Aborted, BlackOccupied: false, WhiteOccupied: false,
                AbortReason: LanMatchAbortReason.BothPlayersLeft, Board.Outcome: MatchOutcome.InProgress });
    }

    private sealed class ReconnectClock : TimeProvider
    {
        private ReconnectTimer? timer;
        private long timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => timestamp;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            timer = new(callback, state);
            return timer;
        }

        public void Advance(TimeSpan duration, bool publishTick = true)
        {
            timestamp += duration.Ticks;
            if (publishTick)
            {
                timer?.Tick();
            }
        }
    }

    private sealed class ReconnectTimer(TimerCallback callback, object? state) : ITimer
    {
        private bool disposed;

        public void Tick()
        {
            if (!disposed)
            {
                callback(state);
            }
        }

        public bool Change(TimeSpan dueTime, TimeSpan period) => throw new NotSupportedException();
        public void Dispose() => disposed = true;
        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
