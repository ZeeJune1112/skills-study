namespace Gomoku.Core;

public sealed class RoomSession
{
    private readonly Stone?[,] board = new Stone?[RoomSnapshot.BoardSize, RoomSnapshot.BoardSize];
    private readonly List<Move> moves = [];

    public RoomSnapshot Snapshot { get; private set; } = new(Stone.Black, MatchOutcome.InProgress, []);
    public AiDifficulty Difficulty { get; private set; }

    public RoomSession() : this(AiDifficulty.Easy)
    {
    }

    public RoomSession(AiDifficulty difficulty)
    {
        if (!Enum.IsDefined(difficulty))
        {
            throw new ArgumentOutOfRangeException(nameof(difficulty));
        }

        Difficulty = difficulty;
    }

    public bool TrySetAiDifficulty(AiDifficulty difficulty)
    {
        if (Snapshot.Outcome == MatchOutcome.InProgress || !Enum.IsDefined(difficulty))
        {
            return false;
        }

        Difficulty = difficulty;
        return true;
    }

    public bool TryPlace(Stone stone, BoardPoint point)
    {
        if (Snapshot.Turn != stone || !BoardRules.IsOnBoard(point.Row, point.Column) ||
            board[point.Row, point.Column] is not null)
        {
            return false;
        }

        board[point.Row, point.Column] = stone;
        moves.Add(new(stone, point));
        if (BoardRules.HasFive(board, stone, point))
        {
            SetSnapshot(null, stone == Stone.Black ? MatchOutcome.BlackWon : MatchOutcome.WhiteWon);
        }
        else if (moves.Count == board.Length)
        {
            SetSnapshot(null, MatchOutcome.Draw);
        }
        else
        {
            SetSnapshot(BoardRules.OpponentOf(stone));
        }

        return true;
    }

    public bool TryUndo(Stone requester)
    {
        if (Snapshot.Outcome != MatchOutcome.InProgress)
        {
            return false;
        }

        var lastOwnMove = moves.FindLastIndex(move => move.Stone == requester);
        if (lastOwnMove < 0)
        {
            return false;
        }

        for (var index = lastOwnMove; index < moves.Count; index++)
        {
            var point = moves[index].Point;
            board[point.Row, point.Column] = null;
        }

        moves.RemoveRange(lastOwnMove, moves.Count - lastOwnMove);
        SetSnapshot(requester);
        return true;
    }

    public void Restart()
    {
        Array.Clear(board);
        moves.Clear();
        SetSnapshot(Stone.Black);
    }

    public bool TryPlayAi()
    {
        var position = Snapshot;
        return position.Turn == Stone.White && GomokuAi.ChooseMove(position, Difficulty) is { } point &&
            TryPlayAi(position, point);
    }

    public bool TryPlayAi(RoomSnapshot position, BoardPoint point, CancellationToken cancellationToken = default) =>
        !cancellationToken.IsCancellationRequested && ReferenceEquals(position, Snapshot) &&
        TryPlace(Stone.White, point);

    private void SetSnapshot(Stone? turn, MatchOutcome outcome = MatchOutcome.InProgress)
    {
        Snapshot = new(turn, outcome, Array.AsReadOnly(moves.ToArray()));
    }
}
