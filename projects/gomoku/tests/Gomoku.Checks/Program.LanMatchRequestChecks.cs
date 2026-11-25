using System.Net;
using Gomoku.Core;
using Gomoku.Core.Lan;

namespace Gomoku.Checks;

internal static partial class Program
{
    [Check]
    private static void LanPlayersConfirmOneOrTwoMoveUndo() =>
        CheckLanUndoAsync().GetAwaiter().GetResult();

    [Check]
    private static void LanRestartRequiresTheOtherPlayerAndSynchronizesObservers() =>
        CheckLanRestartAsync().GetAwaiter().GetResult();

    [Check]
    private static void LanMatchRequestsExpireWhenTheBoardOrSeatsChange() =>
        CheckLanMatchRequestLifetimeAsync().GetAwaiter().GetResult();

    private static async Task CheckLanUndoAsync()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var room = new TcpLanRoomHost();
        var endpoint = new IPEndPoint(IPAddress.Loopback, room.Port);
        using var guest = await TcpLanRoomConnection.ConnectAsync(endpoint, room.RoomId, deadline.Token);
        using var observer = await TcpLanRoomConnection.ConnectAsync(endpoint, room.RoomId, deadline.Token);
        var host = room.Connection;
        await using var hostEvents = host.ReadEventsAsync(deadline.Token).GetAsyncEnumerator();
        await using var guestEvents = guest.ReadEventsAsync(deadline.Token).GetAsyncEnumerator();
        await using var observerEvents = observer.ReadEventsAsync(deadline.Token).GetAsyncEnumerator();

        await guest.SendAsync(new LanRoomCommand.SelectSeat(Stone.White), deadline.Token);
        await ExpectRoomAsync(hostEvents, state => state.WhiteOccupied);
        await host.SendAsync(new LanRoomCommand.StartMatch(), deadline.Token);
        await host.SendAsync(new LanRoomCommand.PlaceStone(7, 7), deadline.Token);
        await host.SendAsync(new LanRoomCommand.RequestMatchChange(LanMatchChange.Undo), deadline.Token);

        var one = await ExpectRoomAsync(guestEvents, state => state.MatchRequest is
            { Change: LanMatchChange.Undo, Requester: Stone.Black, Status: LanMatchRequestStatus.Pending });
        var oneId = one.MatchRequest!.Id;
        Require(one.Board is { Moves.Count: 1, Turn: Stone.White }, "An undo request leaves the board as it was.");
        await ExpectRoomAsync(hostEvents, state => state.MatchRequest?.Id == oneId);
        await guest.SendAsync(new LanRoomCommand.RespondToMatchRequest(oneId, Confirm: true), deadline.Token);
        await ExpectRoomAsync(hostEvents, state => state is
            { Board: { Moves.Count: 0, Turn: Stone.Black }, MatchRequest.Status: LanMatchRequestStatus.Confirmed });
        await ExpectRoomAsync(guestEvents, state => state is
            { Board: { Moves.Count: 0, Turn: Stone.Black }, MatchRequest.Status: LanMatchRequestStatus.Confirmed });

        await host.SendAsync(new LanRoomCommand.PlaceStone(7, 7), deadline.Token);
        await ExpectRoomAsync(guestEvents, state => state.Board.Moves.Count == 1);
        await guest.SendAsync(new LanRoomCommand.PlaceStone(7, 8), deadline.Token);
        await ExpectRoomAsync(hostEvents, state => state.Board.Moves.Count == 2);
        await host.SendAsync(new LanRoomCommand.PlaceStone(8, 7), deadline.Token);
        await guest.SendAsync(new LanRoomCommand.RequestMatchChange(LanMatchChange.Undo), deadline.Token);
        var two = await ExpectRoomAsync(hostEvents, state => state.MatchRequest is
            { Change: LanMatchChange.Undo, Requester: Stone.White, Status: LanMatchRequestStatus.Pending });
        await host.SendAsync(new LanRoomCommand.RespondToMatchRequest(two.MatchRequest!.Id, Confirm: true), deadline.Token);

        foreach (var events in new[] { hostEvents, guestEvents, observerEvents })
        {
            var undone = await ExpectRoomAsync(events, state => state.MatchRequest is
                { Requester: Stone.White, Status: LanMatchRequestStatus.Confirmed });
            Require(undone.Board.Turn == Stone.White &&
                undone.Board.Moves.SequenceEqual([new Move(Stone.Black, new(7, 7))]),
                "White's undo removes its own move and Black's reply for every room member.");
        }
    }

    private static async Task CheckLanRestartAsync()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var room = new TcpLanRoomHost();
        var endpoint = new IPEndPoint(IPAddress.Loopback, room.Port);
        using var guest = await TcpLanRoomConnection.ConnectAsync(endpoint, room.RoomId, deadline.Token);
        using var observer = await TcpLanRoomConnection.ConnectAsync(endpoint, room.RoomId, deadline.Token);
        var host = room.Connection;
        await using var hostEvents = host.ReadEventsAsync(deadline.Token).GetAsyncEnumerator();
        await using var guestEvents = guest.ReadEventsAsync(deadline.Token).GetAsyncEnumerator();
        await using var observerEvents = observer.ReadEventsAsync(deadline.Token).GetAsyncEnumerator();
        var restart = new LanRoomCommand.RequestMatchChange(LanMatchChange.Restart);
        var undo = new LanRoomCommand.RequestMatchChange(LanMatchChange.Undo);

        await guest.SendAsync(new LanRoomCommand.SelectSeat(Stone.White), deadline.Token);
        await ExpectRoomAsync(hostEvents, state => state.WhiteOccupied);
        await ExpectRejectedAsync(host, hostEvents, restart, LanRoomError.MatchRequestUnavailable);
        await host.SendAsync(new LanRoomCommand.StartMatch(), deadline.Token);
        await host.SendAsync(new LanRoomCommand.PlaceStone(7, 7), deadline.Token);
        await ExpectRoomAsync(guestEvents, state => state.Board.Moves.Count == 1);
        await ExpectRejectedAsync(guest, guestEvents, undo, LanRoomError.MatchRequestUnavailable);
        await ExpectRejectedAsync(observer, observerEvents, restart, LanRoomError.NoSeat);

        await guest.SendAsync(restart, deadline.Token);
        var requested = await ExpectRoomAsync(hostEvents, state => state.MatchRequest is
            { Change: LanMatchChange.Restart, Requester: Stone.White, Status: LanMatchRequestStatus.Pending });
        var firstId = requested.MatchRequest!.Id;
        var confirm = new LanRoomCommand.RespondToMatchRequest(firstId, Confirm: true);
        await ExpectRejectedAsync(observer, observerEvents, confirm, LanRoomError.NoSeat);
        await ExpectRejectedAsync(guest, guestEvents, confirm, LanRoomError.MatchRequestOpponentOnly);
        await ExpectRejectedAsync(host, hostEvents, undo, LanRoomError.MatchRequestPending);
        await host.SendAsync(confirm with { Confirm = false }, deadline.Token);
        foreach (var events in new[] { hostEvents, guestEvents })
        {
            var refused = await ExpectRoomAsync(events, state => state.MatchRequest is
                { Status: LanMatchRequestStatus.Refused });
            Require(refused.Board.Turn == Stone.White &&
                refused.Board.Moves.SequenceEqual([new Move(Stone.Black, new(7, 7))]),
                "Refusal preserves the board and turn for both players.");
        }

        await guest.SendAsync(restart, deadline.Token);
        var requestedAgain = await ExpectRoomAsync(hostEvents, state => state.MatchRequest is
            { Status: LanMatchRequestStatus.Pending });
        var secondId = requestedAgain.MatchRequest!.Id;
        Require(secondId != firstId, "Each request needs a fresh ID.");
        await ExpectRejectedAsync(host, hostEvents, confirm, LanRoomError.MatchRequestExpired);
        await host.SendAsync(confirm with { RequestId = secondId }, deadline.Token);
        foreach (var events in new[] { hostEvents, guestEvents, observerEvents })
        {
            await ExpectRoomAsync(events, state => state is
            {
                Phase: LanRoomPhase.Playing,
                Board: { Moves.Count: 0, Turn: Stone.Black },
                MatchRequest.Status: LanMatchRequestStatus.Confirmed
            });
        }
    }

    private static async Task CheckLanMatchRequestLifetimeAsync()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var room = new LanRoomAuthority();
        using var guest = room.Join(room.RoomId);
        var host = room.Host;
        await using var hostEvents = host.ReadEventsAsync(deadline.Token).GetAsyncEnumerator();
        await using var guestEvents = guest.ReadEventsAsync(deadline.Token).GetAsyncEnumerator();
        var undo = new LanRoomCommand.RequestMatchChange(LanMatchChange.Undo);
        var restart = new LanRoomCommand.RequestMatchChange(LanMatchChange.Restart);

        await guest.SendAsync(new LanRoomCommand.SelectSeat(Stone.White), deadline.Token);
        await host.SendAsync(new LanRoomCommand.StartMatch(), deadline.Token);
        await host.SendAsync(new LanRoomCommand.PlaceStone(0, 0), deadline.Token);
        await host.SendAsync(undo, deadline.Token);
        var pending = await ExpectRoomAsync(guestEvents, state => state.MatchRequest?.Status == LanMatchRequestStatus.Pending);
        var stale = new LanRoomCommand.RespondToMatchRequest(pending.MatchRequest!.Id, Confirm: true);
        await guest.SendAsync(new LanRoomCommand.PlaceStone(14, 0), deadline.Token);
        await ExpectRoomAsync(hostEvents, state => state is
            { Board: { Moves.Count: 2, Turn: Stone.Black }, MatchRequest: null });
        await ExpectRejectedAsync(guest, guestEvents, stale, LanRoomError.MatchRequestExpired);

        for (var column = 1; column < 4; column++)
        {
            await host.SendAsync(new LanRoomCommand.PlaceStone(0, column), deadline.Token);
            await guest.SendAsync(new LanRoomCommand.PlaceStone(14, column), deadline.Token);
        }

        await guest.SendAsync(undo, deadline.Token);
        var finalUndo = await ExpectRoomAsync(hostEvents, state => state.MatchRequest is
            { Requester: Stone.White, Status: LanMatchRequestStatus.Pending });
        await host.SendAsync(new LanRoomCommand.PlaceStone(0, 4), deadline.Token);
        await ExpectRoomAsync(guestEvents, state => state is
            { Phase: LanRoomPhase.Finished, Board.Outcome: MatchOutcome.BlackWon, MatchRequest: null });
        await ExpectRejectedAsync(host, hostEvents, stale with { RequestId = finalUndo.MatchRequest!.Id },
            LanRoomError.MatchRequestExpired);
        await ExpectRejectedAsync(host, hostEvents, undo, LanRoomError.MatchRequestUnavailable);

        await guest.SendAsync(restart, deadline.Token);
        var rematch = await ExpectRoomAsync(hostEvents, state => state is
            { Phase: LanRoomPhase.Finished, MatchRequest: { Change: LanMatchChange.Restart, Status: LanMatchRequestStatus.Pending } });
        var confirmRematch = stale with { RequestId = rematch.MatchRequest!.Id };
        await host.SendAsync(confirmRematch, deadline.Token);
        foreach (var events in new[] { hostEvents, guestEvents })
        {
            await ExpectRoomAsync(events, state => state is
            {
                BlackOccupied: true, WhiteOccupied: true, Phase: LanRoomPhase.Playing,
                Board: { Moves.Count: 0, Turn: Stone.Black, Outcome: MatchOutcome.InProgress },
                MatchRequest.Status: LanMatchRequestStatus.Confirmed
            } && state.RoomId == room.RoomId);
        }

        await ExpectRejectedAsync(host, hostEvents, confirmRematch, LanRoomError.MatchRequestExpired);
        await host.SendAsync(new LanRoomCommand.PlaceStone(0, 0), deadline.Token);
        await host.SendAsync(restart, deadline.Token);
        await ExpectRoomAsync(guestEvents, state => state is
            { Board.Moves.Count: 1, MatchRequest.Status: LanMatchRequestStatus.Pending });
        guest.Dispose();
        await ExpectRoomAsync(hostEvents, state => state is
            { Phase: LanRoomPhase.Aborted, WhiteOccupied: false, MatchRequest: null, CanRequestMatchChange: false });
    }
}
