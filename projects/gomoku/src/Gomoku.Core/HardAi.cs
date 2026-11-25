namespace Gomoku.Core;

internal static class HardAi
{
    private const int Win = 100_000;
    private const int CandidateLimit = 10;

    public static BoardPoint? ChooseMove(Stone?[,] board, List<BoardPoint> empty, Stone turn,
        CancellationToken cancellationToken) =>
        Search(board, empty, turn, plies: 2, cancellationToken).Point;

    private static (BoardPoint? Point, int Score) Search(Stone?[,] board, List<BoardPoint> empty, Stone turn,
        int plies, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var opponent = BoardRules.OpponentOf(turn);
        var candidates = new List<(BoardPoint Point, int Priority)>();
        BoardPoint? block = null;
        var threats = 0;
        var strongestAttack = 0;
        var strongestDefense = 0;

        foreach (var point in empty)
        {
            if (board[point.Row, point.Column] is not null)
            {
                continue;
            }

            var attack = ScorePlacement(board, turn, point);
            if (attack == Win)
            {
                return (point, Win + plies);
            }

            var defense = ScorePlacement(board, opponent, point);
            if (defense == Win)
            {
                block ??= point;
                threats++;
            }

            strongestAttack = Math.Max(strongestAttack, attack);
            strongestDefense = Math.Max(strongestDefense, defense);
            if (plies > 0)
            {
                candidates.Add((point, Math.Max(attack, defense) + attack / 4));
            }
        }

        if (threats > 1)
        {
            return (block, -Win - plies);
        }

        if (plies == 0)
        {
            return (null, strongestAttack - Math.Min(strongestDefense, 1_000));
        }

        IEnumerable<BoardPoint> replies = block is { } required
            ? [required]
            : candidates.OrderByDescending(candidate => candidate.Priority)
                .ThenBy(candidate => GomokuAi.DistanceFromCenter(candidate.Point))
                .Take(CandidateLimit)
                .Select(candidate => candidate.Point);

        BoardPoint? bestPoint = null;
        var bestScore = int.MinValue;
        foreach (var point in replies)
        {
            board[point.Row, point.Column] = turn;
            var score = -Search(board, empty, opponent, plies - 1, cancellationToken).Score;
            board[point.Row, point.Column] = null;
            if (score > bestScore)
            {
                bestPoint = point;
                bestScore = score;
            }
        }

        return (bestPoint, bestPoint is null ? 0 : bestScore);
    }

    private static int ScorePlacement(Stone?[,] board, Stone stone, BoardPoint point)
    {
        var score = 0;
        foreach (var direction in BoardRules.Directions)
        {
            var forward = BoardRules.CountAlong(board, stone, point, direction.Row, direction.Column);
            var backward = BoardRules.CountAlong(board, stone, point, -direction.Row, -direction.Column);
            var length = 1 + forward + backward;
            if (length >= 5)
            {
                return Win;
            }

            var openEnds = OpenEnd(board, point, direction, forward + 1)
                + OpenEnd(board, point, direction, -backward - 1);
            score += (length, openEnds) switch
            {
                (4, 2) => 5_000,
                (4, 1) => 800,
                (3, 2) => 180,
                (3, 1) => 32,
                (2, 2) => 16,
                (2, 1) => 4,
                _ => 0
            };
        }

        return score;
    }

    private static int OpenEnd(Stone?[,] board, BoardPoint point, BoardPoint direction, int offset)
    {
        var row = point.Row + direction.Row * offset;
        var column = point.Column + direction.Column * offset;
        return BoardRules.IsOnBoard(row, column) && board[row, column] is null ? 1 : 0;
    }
}
