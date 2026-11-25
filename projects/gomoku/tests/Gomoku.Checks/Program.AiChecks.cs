using Gomoku.Core;

namespace Gomoku.Checks;

internal static partial class Program
{
    [Check]
    private static void NormalAiWinsBeforeBlockingAnImmediateThreat()
    {
        foreach (var difficulty in new[] { AiDifficulty.Normal, AiDifficulty.Hard })
        {
            var room = SetUpAiTurn(difficulty, [(0, 0), (0, 1), (0, 2), (0, 3), (7, 7)],
                [(14, 0), (14, 1), (14, 2), (14, 3)]);

            Require(room.TryPlayAi() && room.Snapshot is { Outcome: MatchOutcome.WhiteWon, Turn: null } &&
                room.Snapshot.Moves[^1] == new Move(Stone.White, new(14, 4)),
                $"{difficulty} should complete White's five even when Black threatens to win.");
        }
    }

    [Check]
    private static void NormalAiBlocksAnImmediateDiagonalWin()
    {
        var room = SetUpAiTurn(AiDifficulty.Normal, [(2, 14), (3, 13), (5, 11), (6, 10)],
            [(7, 7), (9, 9), (11, 11)]);

        Require(room.TryPlayAi() && room.Snapshot is { Outcome: MatchOutcome.InProgress, Turn: Stone.Black } &&
            room.Snapshot.Moves[^1] == new Move(Stone.White, new(4, 12)),
            "Normal should fill the gap in Black's diagonal four.");
    }

    [Check]
    private static void HardAiCreatesAWinThatSurvivesEitherBlock()
    {
        foreach (var blockLeft in new[] { true, false })
        {
            var room = SetUpAiTurn(AiDifficulty.Hard, [(1, 1), (1, 3), (3, 1), (3, 3)],
                [(12, 3), (12, 4), (12, 5)]);
            Require(room.TryPlayAi() && room.Snapshot.Outcome == MatchOutcome.InProgress,
                "White has no immediate win.");
            var extension = room.Snapshot.Moves[^1].Point;
            Require(extension is { Row: 12, Column: 2 or 6 }, "Hard should extend White's open three at either end.");

            var left = extension.Column == 2 ? 1 : 2;
            var right = extension.Column == 2 ? 6 : 7;
            Play(room, Stone.Black, new(12, blockLeft ? left : right));
            Require(room.TryPlayAi() && room.Snapshot.Outcome == MatchOutcome.WhiteWon,
                "White should win at the other end even when Black blocks one.");
        }
    }

    [Check]
    private static void HardAiPreventsAnOpenFourBeforeItExists()
    {
        var room = SetUpAiTurn(AiDifficulty.Hard, [(2, 3), (2, 4), (2, 5), (13, 13)],
            [(11, 1), (11, 3), (13, 1)]);

        Require(room.TryPlayAi() && room.Snapshot.Turn == Stone.Black &&
            room.Snapshot.Moves[^1].Point is { Row: 2, Column: 2 or 6 },
            "Hard should block Black's open three before either extension creates two winning replies.");
    }

    [Check]
    private static void AiReplyFromBeforeUndoOrRestartCannotPlayOnARecreatedPosition()
    {
        foreach (var undo in new[] { true, false })
        {
            var room = new RoomSession(AiDifficulty.Hard);
            var opening = new BoardPoint(7, 7);
            var reply = new BoardPoint(7, 8);
            Play(room, Stone.Black, opening);
            var earlier = room.Snapshot;
            if (undo)
            {
                Require(room.TryUndo(Stone.Black), "Black can undo while the AI is thinking.");
            }
            else
            {
                room.Restart();
            }

            Play(room, Stone.Black, opening);
            Require(!room.TryPlayAi(earlier, reply) && room.Snapshot is { Turn: Stone.White, Moves.Count: 1 },
                "A reply to the old turn cannot land even when Black replays the same move.");
            var current = room.Snapshot;
            Require(room.TryPlayAi(current, reply) && room.Snapshot is { Turn: Stone.Black, Moves.Count: 2 },
                "A reply to the current turn should still land.");
            Require(!room.TryPlayAi(current, new(8, 7)), "A turn should accept at most one AI reply.");
        }
    }

    [Check]
    private static void AiReplyCannotLandAfterThePlayerLeaves()
    {
        var room = new RoomSession(AiDifficulty.Hard);
        Play(room, Stone.Black, new(7, 7));
        var position = room.Snapshot;
        using var roomLifetime = new CancellationTokenSource();
        roomLifetime.Cancel();

        Require(!room.TryPlayAi(position, new(7, 8), roomLifetime.Token)
            && room.Snapshot is { Turn: Stone.White, Moves.Count: 1 },
            "A canceled room must reject the reply even while its last position still expects White.");
    }

    private static RoomSession SetUpAiTurn(AiDifficulty difficulty,
        (int Row, int Column)[] black, (int Row, int Column)[] white)
    {
        var room = new RoomSession(difficulty);
        for (var index = 0; index < black.Length; index++)
        {
            Play(room, Stone.Black, new(black[index].Row, black[index].Column));
            if (index < white.Length)
            {
                Play(room, Stone.White, new(white[index].Row, white[index].Column));
            }
        }

        return room;
    }

    [Check]
    private static void AiDifficultyIsLockedUntilTheMatchEnds()
    {
        var room = new RoomSession(AiDifficulty.Hard);
        Require(room.Difficulty == AiDifficulty.Hard, "The first match should use the room's selected difficulty.");
        Require(!room.TrySetAiDifficulty(AiDifficulty.Easy), "The first match starts as soon as the room opens.");
        Play(room, Stone.Black, new(0, 0));
        Require(!room.TrySetAiDifficulty(AiDifficulty.Normal), "Difficulty should be locked after play starts.");
        room.Restart();
        Require(room.Difficulty == AiDifficulty.Hard, "An in-progress restart should retain difficulty.");

        for (var column = 0; column < 5; column++)
        {
            Play(room, Stone.Black, new(0, column));
            if (column < 4)
            {
                Play(room, Stone.White, new(14, column));
            }
        }

        Require(room.Snapshot.Outcome == MatchOutcome.BlackWon, "The match should be over.");
        Require(room.TrySetAiDifficulty(AiDifficulty.Normal), "The host can select difficulty between matches.");
        Require(room.Snapshot.Outcome == MatchOutcome.BlackWon, "Selecting difficulty should leave the result visible.");
        room.Restart();
        Require(room.Difficulty == AiDifficulty.Normal && room.Snapshot is { Turn: Stone.Black, Moves.Count: 0 },
            "The next match should begin with the newly selected difficulty.");
        Require(!room.TrySetAiDifficulty(AiDifficulty.Easy), "The next match should lock its difficulty immediately.");
    }
}
