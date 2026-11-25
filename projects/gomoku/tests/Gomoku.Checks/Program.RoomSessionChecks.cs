using System.Text.Json;
using Gomoku.Core;

namespace Gomoku.Checks;

internal static partial class Program
{
    [Check]
    private static void PlacesOnlyOnEmptyPointsInTurn()
    {
        var room = new RoomSession();
        var center = new BoardPoint(7, 7);
        Require(room.Snapshot is { Turn: Stone.Black, Outcome: MatchOutcome.InProgress },
            "The room should open with Black to play.");
        Require(!room.TryPlace(Stone.White, center), "White cannot open.");
        foreach (var point in new BoardPoint[] { new(-1, 7), new(7, -1), new(15, 7), new(7, 15) })
        {
            Require(!room.TryPlace(Stone.Black, point), $"Point {point} is outside the board.");
        }

        Play(room, Stone.Black, center);
        Require(room.Snapshot.Turn == Stone.White, "White should follow Black.");
        Require(!room.TryPlace(Stone.White, center), "An occupied point cannot be played.");
        Require(!room.TryPlace(Stone.Black, new(0, 0)), "Black cannot play twice.");
        Play(room, Stone.White, new(14, 14));
        Require(room.Snapshot.Turn == Stone.Black, "Black should follow White.");
        Require(room.Snapshot.Moves.SequenceEqual(
            [new Move(Stone.Black, center), new Move(Stone.White, new(14, 14))]),
            "Only accepted moves should appear, in order.");
    }

    private static void Play(RoomSession room, Stone stone, BoardPoint point)
    {
        Require(room.TryPlace(stone, point), $"Expected a legal {stone} move at {point}.");
    }

    [Check]
    private static void WinsWithFiveOrMoreInAnyDirection()
    {
        foreach (var direction in new BoardPoint[] { new(0, 1), new(1, 0), new(1, 1), new(1, -1) })
        {
            VerifyBlackWin(direction, [0, 1, 2, 3, 4]);
            VerifyBlackWin(direction, [0, 1, 2, 4, 5, 3]);
        }

        var whiteRoom = new RoomSession();
        for (var index = 0; index < 5; index++)
        {
            Play(whiteRoom, Stone.Black, new(0, index * 2));
            Play(whiteRoom, Stone.White, new(14, index));
        }

        Require(whiteRoom.Snapshot is { Outcome: MatchOutcome.WhiteWon, Turn: null },
            "White should also win with five.");
    }

    private static void VerifyBlackWin(BoardPoint direction, int[] offsets)
    {
        var room = new RoomSession();
        for (var index = 0; index < offsets.Length; index++)
        {
            var offset = offsets[index];
            Play(room, Stone.Black, new(4 + offset * direction.Row, 7 + offset * direction.Column));
            if (index + 1 < offsets.Length)
            {
                Require(room.Snapshot.Outcome == MatchOutcome.InProgress, "There is no five yet.");
                Play(room, Stone.White, new(0, index * 2));
            }
        }

        Require(room.Snapshot is { Outcome: MatchOutcome.BlackWon, Turn: null },
            $"Black should win with {offsets.Length} stones in direction {direction}.");
        Require(!room.TryPlace(Stone.White, new(14, 14)), "A finished match rejects more moves.");
    }

    [Check]
    private static void DrawsOnAFullBoardWithoutFive()
    {
        var black = new List<BoardPoint>();
        var white = new List<BoardPoint>();
        for (var row = 0; row < 15; row++)
        {
            for (var column = 0; column < 15; column++)
            {
                var points = (column + 2 * row) % 4 < 2 ? black : white;
                points.Add(new(row, column));
            }
        }

        var room = new RoomSession();
        for (var index = 0; index < 112; index++)
        {
            Play(room, Stone.Black, black[index]);
            Play(room, Stone.White, white[index]);
        }

        Require(room.Snapshot.Outcome == MatchOutcome.InProgress, "One point remains open.");
        Play(room, Stone.Black, black[112]);
        Require(room.Snapshot is { Outcome: MatchOutcome.Draw, Turn: null, Moves.Count: 225 },
            "A full board without five is a draw.");
        Require(!room.TryUndo(Stone.Black), "A finished match cannot be undone.");
    }

    [Check]
    private static void UndoReturnsToBeforeTheRequestersLastMove()
    {
        var room = new RoomSession();
        var center = new BoardPoint(7, 7);
        var reply = new BoardPoint(7, 8);
        Require(!room.TryUndo(Stone.Black), "There is nothing to undo at the start.");
        Play(room, Stone.Black, center);
        Require(!room.TryUndo(Stone.White), "White has no move of its own to undo.");
        Require(room.TryUndo(Stone.Black), "Black can undo while waiting for White.");
        Require(room.Snapshot is { Turn: Stone.Black, Moves.Count: 0 }, "Undo should remove one move.");

        Play(room, Stone.Black, center);
        Play(room, Stone.White, reply);
        var earlierMoves = room.Snapshot.Moves.ToArray();
        var laterMove = new BoardPoint(8, 7);
        Play(room, Stone.Black, laterMove);
        Play(room, Stone.White, new(8, 8));
        Require(room.TryUndo(Stone.Black), "Black can undo after White replies.");
        Require(room.Snapshot.Turn == Stone.Black && room.Snapshot.Moves.SequenceEqual(earlierMoves),
            "Undo should remove both latest moves and keep the opening.");
        Play(room, Stone.Black, laterMove);
    }

    [Check]
    private static void RestartsDuringAndAfterAMatch()
    {
        var room = new RoomSession();
        Play(room, Stone.Black, new(0, 0));
        room.Restart();
        Require(room.Snapshot is { Turn: Stone.Black, Outcome: MatchOutcome.InProgress, Moves.Count: 0 },
            "Restart should reset a match in progress.");
        for (var index = 0; index < 5; index++)
        {
            Play(room, Stone.Black, new(0, index));
            if (index < 4)
            {
                Play(room, Stone.White, new(14, index));
            }
        }

        Require(room.Snapshot.Outcome == MatchOutcome.BlackWon, "Black should have won the match.");
        room.Restart();
        Require(room.Snapshot is { Turn: Stone.Black, Outcome: MatchOutcome.InProgress, Moves.Count: 0 },
            "Rematch should open an empty board with Black to play.");
        Play(room, Stone.Black, new(0, 0));
    }

    [Check]
    private static void AiPlaysOneLegalWhiteMove()
    {
        var room = new RoomSession();
        var center = new BoardPoint(7, 7);
        Require(!room.TryPlayAi(), "White AI cannot open the match.");
        Play(room, Stone.Black, center);
        Require(room.TryPlayAi(), "White AI should reply.");
        Require(room.Snapshot is { Turn: Stone.Black, Moves.Count: 2 }, "AI should play exactly one move.");
        var reply = room.Snapshot.Moves[1];
        Require(reply.Stone == Stone.White && reply.Point != center
            && reply.Point.Row is >= 0 and < 15 && reply.Point.Column is >= 0 and < 15,
            "The AI reply should be White on an empty intersection.");
        Require(!room.TryPlayAi(), "White AI cannot move on Black's turn.");
        Require(room.TryUndo(Stone.Black) && room.Snapshot is { Turn: Stone.Black, Moves.Count: 0 },
            "Undo should remove the actual AI reply together with the human move.");
    }

    [Check]
    private static void CapturedSnapshotRoundTripsAfterTheRoomChanges()
    {
        var room = new RoomSession();
        Play(room, Stone.Black, new(7, 7));
        Play(room, Stone.White, new(7, 8));
        var snapshot = room.Snapshot;
        room.Restart();

        var copy = JsonSerializer.Deserialize<RoomSnapshot>(JsonSerializer.Serialize(snapshot));
        Require(copy is { Turn: Stone.Black, Outcome: MatchOutcome.InProgress }
            && copy.Moves.SequenceEqual([new Move(Stone.Black, new(7, 7)), new Move(Stone.White, new(7, 8))]),
            "A captured snapshot must preserve its ordered moves, turn, and outcome through JSON.");
    }
}
