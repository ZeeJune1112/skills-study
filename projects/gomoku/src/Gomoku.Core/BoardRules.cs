namespace Gomoku.Core;

internal static class BoardRules
{
    private static readonly BoardPoint[] directions = [new(0, 1), new(1, 0), new(1, 1), new(1, -1)];

    public static ReadOnlySpan<BoardPoint> Directions => directions;

    public static Stone OpponentOf(Stone stone) => stone == Stone.Black ? Stone.White : Stone.Black;

    public static bool IsOnBoard(int row, int column) =>
        row >= 0 && row < RoomSnapshot.BoardSize && column >= 0 && column < RoomSnapshot.BoardSize;

    public static bool HasFive(Stone?[,] board, Stone stone, BoardPoint point)
    {
        foreach (var direction in Directions)
        {
            var length = 1
                + CountAlong(board, stone, point, direction.Row, direction.Column)
                + CountAlong(board, stone, point, -direction.Row, -direction.Column);
            if (length >= 5)
            {
                return true;
            }
        }

        return false;
    }

    public static int CountAlong(Stone?[,] board, Stone stone, BoardPoint point, int rowStep, int columnStep)
    {
        var row = point.Row + rowStep;
        var column = point.Column + columnStep;
        var count = 0;
        while (IsOnBoard(row, column) && board[row, column] == stone)
        {
            count++;
            row += rowStep;
            column += columnStep;
        }

        return count;
    }
}
