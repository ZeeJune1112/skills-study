using System.Net;
using Gomoku.Core;
using Gomoku.Core.Lan;

namespace Gomoku.Checks;

internal static partial class Program
{
    [Check]
    private static void LanTcpPlaysAndObservesACompleteMatch() =>
        CheckLanTcpAsync().GetAwaiter().GetResult();

    private static async Task CheckLanTcpAsync()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var room = new TcpLanRoomHost();
        var endpoint = new IPEndPoint(IPAddress.Loopback, room.Port);
        var wrongId = (room.RoomId[0] == '0' ? "1" : "0") + room.RoomId[1..];
        try
        {
            using var wrongRoom = await TcpLanRoomConnection.ConnectAsync(endpoint, wrongId, deadline.Token);
            throw new InvalidOperationException("Joining by the wrong room ID succeeded.");
        }
        catch (LanRoomException error)
        {
            Require(error.Error == LanRoomError.RoomNotFound, "The host should reject a mismatched room ID.");
        }

        using var guest = await TcpLanRoomConnection.ConnectAsync(endpoint, room.RoomId, deadline.Token);
        await using var hostEvents = room.Connection.ReadEventsAsync(deadline.Token).GetAsyncEnumerator();
        await using var guestEvents = guest.ReadEventsAsync(deadline.Token).GetAsyncEnumerator();
        await ExpectRoomAsync(hostEvents, state => state.Seat == Stone.Black);
        var entry = await ExpectRoomAsync(guestEvents, state => state.Phase == LanRoomPhase.Waiting);
        Require(entry.RoomId == room.RoomId && !entry.IsHost && entry.Seat is null, "Guests enter as observers.");
        await guest.SendAsync(new LanRoomCommand.SelectSeat(Stone.White), deadline.Token);
        await ExpectRoomAsync(hostEvents, state => state.WhiteOccupied);
        await ExpectRoomAsync(guestEvents, state => state.Seat == Stone.White);
        await ExpectRejectedAsync(guest, guestEvents, new LanRoomCommand.StartMatch(), LanRoomError.HostOnly);
        await room.Connection.SendAsync(new LanRoomCommand.StartMatch(), deadline.Token);
        await ExpectRoomAsync(guestEvents, state => state is { Phase: LanRoomPhase.Playing, Board.Turn: Stone.Black });
        await room.Connection.SendAsync(new LanRoomCommand.PlaceStone(0, 0), deadline.Token);
        await ExpectRoomAsync(guestEvents, state => state.Board is { Moves.Count: 1, Turn: Stone.White });

        using var observer = await TcpLanRoomConnection.ConnectAsync(endpoint, room.RoomId, deadline.Token);
        await using var observerEvents = observer.ReadEventsAsync(deadline.Token).GetAsyncEnumerator();
        var observed = await ExpectRoomAsync(observerEvents, _ => true);
        Require(observed is { Seat: null, Board.Turn: Stone.White } &&
            observed.Board.Moves.SequenceEqual([new Move(Stone.Black, new(0, 0))]),
            "A late observer should receive the current match as its first snapshot.");
        await ExpectRejectedAsync(observer, observerEvents,
            new LanRoomCommand.PlaceStone(14, 0), LanRoomError.ObserverCannotPlay);
        await ExpectRejectedAsync(guest, guestEvents, new LanRoomCommand.PlaceStone(0, 0), LanRoomError.IllegalMove);

        for (var column = 0; column < 4; column++)
        {
            await guest.SendAsync(new LanRoomCommand.PlaceStone(14, column), deadline.Token);
            await ExpectRoomAsync(hostEvents, state => state.Board.Moves.Count == (column + 1) * 2);
            await room.Connection.SendAsync(new LanRoomCommand.PlaceStone(0, column + 1), deadline.Token);
            await ExpectRoomAsync(guestEvents, state => state.Board.Moves.Count == (column + 1) * 2 + 1);
        }

        var hostEnd = await ExpectRoomAsync(hostEvents, state => state.Phase == LanRoomPhase.Finished);
        var observerEnd = await ExpectRoomAsync(observerEvents, state => state.Phase == LanRoomPhase.Finished);
        var expected = new[]
        {
            new Move(Stone.Black, new(0, 0)), new Move(Stone.White, new(14, 0)),
            new Move(Stone.Black, new(0, 1)), new Move(Stone.White, new(14, 1)),
            new Move(Stone.Black, new(0, 2)), new Move(Stone.White, new(14, 2)),
            new Move(Stone.Black, new(0, 3)), new Move(Stone.White, new(14, 3)),
            new Move(Stone.Black, new(0, 4))
        };
        Require(hostEnd.Board is { Turn: null, Outcome: MatchOutcome.BlackWon } &&
            hostEnd.Board.Moves.SequenceEqual(expected), "The host should report the winning position.");
        Require(observerEnd.Board.Outcome == hostEnd.Board.Outcome &&
            observerEnd.Board.Turn == hostEnd.Board.Turn && observerEnd.Board.Moves.SequenceEqual(expected),
            "The observer should report the same winning position.");
        await ExpectRejectedAsync(guest, guestEvents,
            new LanRoomCommand.PlaceStone(14, 4), LanRoomError.MatchNotPlaying);

        await room.DisposeAsync();
        Require(await guestEvents.MoveNextAsync() &&
            guestEvents.Current is LanRoomEvent.Closed { Reason: LanRoomCloseReason.HostClosed } &&
            !await guestEvents.MoveNextAsync(), "The host should announce disbanding the room before closing TCP.");
    }

    [Check]
    private static void LanTcpReleasesWaitingSeatsAndStopsPlayOnDisconnect() =>
        CheckLanTcpDisconnectAsync().GetAwaiter().GetResult();

    private static async Task CheckLanTcpDisconnectAsync()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var room = new TcpLanRoomHost();
        var endpoint = new IPEndPoint(IPAddress.Loopback, room.Port);
        await using var hostEvents = room.Connection.ReadEventsAsync(deadline.Token).GetAsyncEnumerator();

        using var departing = await TcpLanRoomConnection.ConnectAsync(endpoint, room.RoomId, deadline.Token);
        await departing.SendAsync(new LanRoomCommand.SelectSeat(Stone.White), deadline.Token);
        await ExpectRoomAsync(hostEvents, state => state.WhiteOccupied);
        departing.Dispose();
        await ExpectRoomAsync(hostEvents, state => state is { WhiteOccupied: false, Phase: LanRoomPhase.Waiting });

        using var replacement = await TcpLanRoomConnection.ConnectAsync(endpoint, room.RoomId, deadline.Token);
        await replacement.SendAsync(new LanRoomCommand.SelectSeat(Stone.White), deadline.Token);
        await ExpectRoomAsync(hostEvents, state => state.WhiteOccupied);
        await room.Connection.SendAsync(new LanRoomCommand.StartMatch(), deadline.Token);
        await room.Connection.SendAsync(new LanRoomCommand.PlaceStone(7, 7), deadline.Token);
        await ExpectRoomAsync(hostEvents, state => state.Board is { Moves.Count: 1, Turn: Stone.White });
        replacement.Dispose();
        await ExpectRoomAsync(hostEvents, state => state is
            { WhiteOccupied: false, Phase: LanRoomPhase.Aborted, AbortReason: LanMatchAbortReason.PlayerDisconnected,
                Board: { Moves.Count: 1, Turn: null, Outcome: MatchOutcome.InProgress } });
        await ExpectRejectedAsync(room.Connection, hostEvents,
            new LanRoomCommand.PlaceStone(7, 8), LanRoomError.MatchNotPlaying);
    }
}
