using Gomoku.Core;
using Gomoku.Core.Lan;

namespace Gomoku.Checks;

internal static partial class Program
{
    [Check]
    private static void LanRoomValidatesSeatsStartAndMoves() =>
        CheckLanRoomAsync().GetAwaiter().GetResult();

    private static async Task CheckLanRoomAsync()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var room = new LanRoomAuthority();
        using var guest = room.Join(room.RoomId);
        using var observer = room.Join(room.RoomId);
        using var host = room.Host;
        await using var hostEvents = host.ReadEventsAsync(deadline.Token).GetAsyncEnumerator();
        await using var guestEvents = guest.ReadEventsAsync(deadline.Token).GetAsyncEnumerator();
        await using var observerEvents = observer.ReadEventsAsync(deadline.Token).GetAsyncEnumerator();

        await ExpectRoomAsync(hostEvents, state => state is
            { IsHost: true, Seat: Stone.Black, Phase: LanRoomPhase.Waiting, WhiteOccupied: false });
        await ExpectRoomAsync(guestEvents, state => state is { IsHost: false, Seat: null });
        await ExpectRejectedAsync(host, hostEvents, new LanRoomCommand.StartMatch(), LanRoomError.SeatsUnfilled);
        await ExpectRejectedAsync(guest, guestEvents, new LanRoomCommand.SelectSeat(Stone.Black), LanRoomError.SeatTaken);
        await host.SendAsync(new LanRoomCommand.WithdrawSeat(), deadline.Token);
        await ExpectRoomAsync(hostEvents, state => state is { Seat: null, BlackOccupied: false });
        await guest.SendAsync(new LanRoomCommand.SelectSeat(Stone.Black), deadline.Token);
        await ExpectRoomAsync(guestEvents, state => state.Seat == Stone.Black);
        await host.SendAsync(new LanRoomCommand.SelectSeat(Stone.White), deadline.Token);
        await ExpectRoomAsync(hostEvents, state => state is { Seat: Stone.White, BlackOccupied: true, WhiteOccupied: true });
        await ExpectRejectedAsync(guest, guestEvents, new LanRoomCommand.StartMatch(), LanRoomError.HostOnly);
        await host.SendAsync(new LanRoomCommand.StartMatch(), deadline.Token);
        await ExpectRoomAsync(observerEvents, state => state is
            { Seat: null, Phase: LanRoomPhase.Playing, Board.Turn: Stone.Black });
        await ExpectRejectedAsync(observer, observerEvents, new LanRoomCommand.PlaceStone(7, 7), LanRoomError.ObserverCannotPlay);
        await ExpectRejectedAsync(host, hostEvents, new LanRoomCommand.PlaceStone(7, 7), LanRoomError.NotYourTurn);
        await ExpectRejectedAsync(guest, guestEvents, new LanRoomCommand.WithdrawSeat(), LanRoomError.SeatsLocked);
        await ExpectRejectedAsync(guest, guestEvents, new LanRoomCommand.PlaceStone(15, 7), LanRoomError.IllegalMove);
        await guest.SendAsync(new LanRoomCommand.PlaceStone(7, 7), deadline.Token);
        await ExpectRoomAsync(hostEvents, state => state.Board is { Turn: Stone.White, Moves.Count: 1 });
        await ExpectRejectedAsync(host, hostEvents, new LanRoomCommand.PlaceStone(7, 7), LanRoomError.IllegalMove);
        await ExpectRoomAsync(guestEvents, state => state.Board is { Turn: Stone.White, Moves.Count: 1 });
    }

    [Check]
    private static void LanRoomCanStartAnotherMatchAfterAnAbort() =>
        CheckLanRoomAfterAbortAsync().GetAwaiter().GetResult();

    private static async Task CheckLanRoomAfterAbortAsync()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var room = new LanRoomAuthority();
        var host = room.Host;
        using var guest = room.Join(room.RoomId);
        await using var hostEvents = host.ReadEventsAsync(deadline.Token).GetAsyncEnumerator();
        await guest.SendAsync(new LanRoomCommand.SelectSeat(Stone.White));
        await host.SendAsync(new LanRoomCommand.StartMatch());
        await host.SendAsync(new LanRoomCommand.PlaceStone(7, 7));
        await guest.SendAsync(new LanRoomCommand.LeaveRoom());
        await ExpectRoomAsync(hostEvents, state => state is
            { Phase: LanRoomPhase.Aborted, WhiteOccupied: false, Board.Moves.Count: 1 });
        await ExpectRejectedAsync(host, hostEvents, new LanRoomCommand.StartMatch(), LanRoomError.SeatsUnfilled);

        using var replacement = room.Join(room.RoomId);
        await using var replacementEvents = replacement.ReadEventsAsync(deadline.Token).GetAsyncEnumerator();
        await ExpectRoomAsync(replacementEvents, state => state.Seat is null);
        await replacement.SendAsync(new LanRoomCommand.SelectSeat(Stone.White));
        await ExpectRoomAsync(replacementEvents, state => state.Seat == Stone.White);
        await ExpectRejectedAsync(replacement, replacementEvents, new LanRoomCommand.StartMatch(), LanRoomError.HostOnly);
        await host.SendAsync(new LanRoomCommand.StartMatch());
        await ExpectRoomAsync(replacementEvents, state => state is
            { Phase: LanRoomPhase.Playing, AbortReason: null, Board: { Moves.Count: 0, Turn: Stone.Black } });
    }

    private static async Task<LanRoomSnapshot> ExpectRoomAsync(
        IAsyncEnumerator<LanRoomEvent> events, Func<LanRoomSnapshot, bool> matches)
    {
        while (await events.MoveNextAsync())
        {
            if (events.Current is LanRoomEvent.State state && matches(state.Snapshot))
            {
                return state.Snapshot;
            }
        }

        throw new InvalidOperationException("The room closed before the expected snapshot.");
    }

    private static async Task ExpectRejectedAsync(
        ILanRoomConnection connection, IAsyncEnumerator<LanRoomEvent> events, LanRoomCommand command, LanRoomError expected)
    {
        await connection.SendAsync(command);
        while (await events.MoveNextAsync())
        {
            if (events.Current is LanRoomEvent.Rejected rejected)
            {
                Require(rejected.Error == expected, $"Expected {expected}, got {rejected.Error}.");
                return;
            }
        }

        throw new InvalidOperationException($"The room closed before rejecting {command}.");
    }
}
