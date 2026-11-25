namespace Gomoku.Core;

public static class GomokuAi
{
    public static BoardPoint? ChooseMove(RoomSnapshot snapshot, AiDifficulty difficulty,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (snapshot is not { Turn: { } turn, Outcome: MatchOutcome.InProgress })
        {
            return null;
        }

        const int size = RoomSnapshot.BoardSize;
        var board = new Stone?[size, size];
        foreach (var move in snapshot.Moves)
        {
            board[move.Point.Row, move.Point.Column] = move.Stone;
        }

        var empty = new List<BoardPoint>();
        for (var row = 0; row < size; row++)
        {
            for (var column = 0; column < size; column++)
            {
                if (board[row, column] is null)
                {
                    empty.Add(new(row, column));
                }
            }
        }

        if (empty.Count == 0)
        {
            return null;
        }

        return difficulty switch
        {
            AiDifficulty.Easy => empty[Random.Shared.Next(empty.Count)],
            AiDifficulty.Normal => ChooseTacticalMove(board, empty, turn),
            AiDifficulty.Hard => HardAi.ChooseMove(board, empty, turn, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(difficulty))
        };
    }

    private static BoardPoint ChooseTacticalMove(Stone?[,] board, List<BoardPoint> empty, Stone turn)
    {
        var opponent = BoardRules.OpponentOf(turn);
        BoardPoint? block = null;
        foreach (var point in empty)
        {
            if (BoardRules.HasFive(board, turn, point))
            {
                return point;
            }

            if (block is null && BoardRules.HasFive(board, opponent, point))
            {
                block = point;
            }
        }

        return block ?? empty.MinBy(DistanceFromCenter);
    }

    internal static int DistanceFromCenter(BoardPoint point)
    {
        const int center = RoomSnapshot.BoardSize / 2;
        return Math.Abs(point.Row - center) + Math.Abs(point.Column - center);
    }
}
