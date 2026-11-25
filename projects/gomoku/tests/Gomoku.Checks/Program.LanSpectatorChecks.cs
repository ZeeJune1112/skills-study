using System.Net;
using Gomoku.Core;
using Gomoku.Core.Lan;

namespace Gomoku.Checks;

internal static partial class Program
{
    [Check]
    private static void LanSpectatorCapacityCountsOnlySpectatorsAndReopensOnDeparture() =>
        CheckSpectatorCapacityAsync().GetAwaiter().GetResult();

    private static async Task CheckSpectatorCapacityAsync()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var defaultRoom = new LanRoomAuthority();
        await using var defaults = defaultRoom.Host.ReadEventsAsync(deadline.Token).GetAsyncEnumerator();
        var initial = await ExpectRoomAsync(defaults, _ => true);
        Require(initial is { SpectatorCount: 0, SpectatorCapacity: 5 },
            "A new room should have five empty spectator seats.");

        await using var room = new TcpLanRoomHost(spectatorCapacity: 1);
        var endpoint = new IPEndPoint(IPAddress.Loopback, room.Port);
        await using var hostEvents = room.Connection.ReadEventsAsync(deadline.Token).GetAsyncEnumerator();
        using var white = await TcpLanRoomConnection.ConnectAsync(endpoint, room.RoomId, deadline.Token);
        await ExpectRoomAsync(hostEvents, state => state is { SpectatorCount: 1, SpectatorCapacity: 1 });
        await white.SendAsync(new LanRoomCommand.SelectSeat(Stone.White), deadline.Token);
        await ExpectRoomAsync(hostEvents, state => state is { WhiteOccupied: true, SpectatorCount: 0 });

        using var spectator = await TcpLanRoomConnection.ConnectAsync(endpoint, room.RoomId, deadline.Token);
        await using var spectatorEvents = spectator.ReadEventsAsync(deadline.Token).GetAsyncEnumerator();
        var entry = await ExpectRoomAsync(spectatorEvents, _ => true);
        Require(entry is { Seat: null, WhiteOccupied: true, SpectatorCount: 1, SpectatorCapacity: 1 },
            "An arriving spectator should see the current seats and count themself.");
        await using var whiteEvents = white.ReadEventsAsync(deadline.Token).GetAsyncEnumerator();
        await ExpectRejectedAsync(white, whiteEvents, new LanRoomCommand.WithdrawSeat(), LanRoomError.RoomFull);

        try
        {
            using var overflow = await TcpLanRoomConnection.ConnectAsync(endpoint, room.RoomId, deadline.Token);
            throw new InvalidOperationException("A visitor entered a room with no free spectator seats.");
        }
        catch (LanRoomException error)
        {
            Require(error.Error == LanRoomError.RoomFull, "A full room should send an explicit rejection.");
        }

        spectator.Dispose();
        await ExpectRoomAsync(hostEvents, state => state is { SpectatorCount: 1 });
        await ExpectRoomAsync(hostEvents, state => state is { SpectatorCount: 0 });
        using var replacement = await TcpLanRoomConnection.ConnectAsync(endpoint, room.RoomId, deadline.Token);
        await ExpectRoomAsync(hostEvents, state => state is { SpectatorCount: 1 });
    }

    [Check]
    private static void LanAiSpectatorsSeeLivePlayUndoResultAndRestart() =>
        CheckAiSpectatorAsync().GetAwaiter().GetResult();

    private static async Task CheckAiSpectatorAsync()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var room = new TcpLanRoomHost(aiDifficulty: AiDifficulty.Normal);
        var host = room.Connection;
        await using var hostEvents = host.ReadEventsAsync(deadline.Token).GetAsyncEnumerator();
        var opened = await ExpectRoomAsync(hostEvents, _ => true);
        Require(opened is { Seat: Stone.Black, WhiteOccupied: true, AiDifficulty: AiDifficulty.Normal,
            Phase: LanRoomPhase.Playing, Board.Turn: Stone.Black }, "AI rooms open with the computer in White.");
        await ExpectRejectedAsync(host, hostEvents, new LanRoomCommand.SetAiDifficulty(AiDifficulty.Hard),
            LanRoomError.AiActionUnavailable);
        await host.SendAsync(new LanRoomCommand.PlaceStone(0, 0), deadline.Token);
        await ExpectRoomAsync(hostEvents, state => state.Board.Moves.Count == 2);

        var endpoint = new IPEndPoint(IPAddress.Loopback, room.Port);
        using var observer = await TcpLanRoomConnection.ConnectAsync(endpoint, room.RoomId, deadline.Token);
        await using var observerEvents = observer.ReadEventsAsync(deadline.Token).GetAsyncEnumerator();
        var entry = await ExpectRoomAsync(observerEvents, _ => true);
        Require(entry is { Seat: null, BlackOccupied: true, WhiteOccupied: true, Board.Turn: Stone.Black,
            AiDifficulty: AiDifficulty.Normal } &&
            entry.Board.Moves.SequenceEqual([new Move(Stone.Black, new(0, 0)), new Move(Stone.White, new(7, 7))]),
            "The first observer snapshot should contain both played stones and the current turn.");
        await ExpectRejectedAsync(observer, observerEvents, new LanRoomCommand.SelectSeat(Stone.White),
            LanRoomError.SeatsLocked);
        await ExpectRejectedAsync(observer, observerEvents, new LanRoomCommand.PlaceStone(0, 2),
            LanRoomError.ObserverCannotPlay);
        await ExpectRejectedAsync(observer, observerEvents, new LanRoomCommand.UndoAiMatch(), LanRoomError.HostOnly);
        await host.SendAsync(new LanRoomCommand.UndoAiMatch(), deadline.Token);
        await ExpectRoomAsync(observerEvents, state => state.Board is { Moves.Count: 0, Turn: Stone.Black });

        await host.SendAsync(new LanRoomCommand.PlaceStone(7, 7), deadline.Token);
        await host.SendAsync(new LanRoomCommand.RestartAiMatch(), deadline.Token);
        await ExpectRoomAsync(observerEvents, state => state.Board is { Moves.Count: 0, Turn: Stone.Black });
        for (var turn = 0; turn < 16; turn++)
        {
            var point = new BoardPoint(turn / 8, turn % 8 * 2);
            await host.SendAsync(new LanRoomCommand.PlaceStone(point.Row, point.Column), deadline.Token);
            var black = await ExpectRoomAsync(observerEvents, _ => true);
            Require(black.Board.Turn == Stone.White && black.Board.Moves.Count == 2 * turn + 1 &&
                black.Board.Moves[^1] == new Move(Stone.Black, point), "The observer should see each Black move.");
            var white = await ExpectRoomAsync(observerEvents, _ => true);
            Require(white.Board.Moves.Count == 2 * turn + 2 && white.Board.Moves[^1].Stone == Stone.White,
                "The observer should see each White reply.");
            if (white.Phase == LanRoomPhase.Finished)
            {
                Require(white.Board is { Outcome: MatchOutcome.WhiteWon, Turn: null },
                    "The observer should see the AI win.");
                await host.SendAsync(new LanRoomCommand.SetAiDifficulty(AiDifficulty.Hard), deadline.Token);
                await ExpectRoomAsync(observerEvents, state => state is
                    { Phase: LanRoomPhase.Finished, AiDifficulty: AiDifficulty.Hard });
                await host.SendAsync(new LanRoomCommand.RestartAiMatch(), deadline.Token);
                await ExpectRoomAsync(observerEvents, state => state is
                    { Phase: LanRoomPhase.Playing, AiDifficulty: AiDifficulty.Hard, Board: { Moves.Count: 0, Turn: Stone.Black } });
                return;
            }
        }

        throw new InvalidOperationException("The AI did not win the prescribed match.");
    }
}
