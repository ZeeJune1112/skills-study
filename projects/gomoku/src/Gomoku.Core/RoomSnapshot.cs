namespace Gomoku.Core;

public enum Stone
{
    Black,
    White
}

public enum MatchOutcome
{
    InProgress,
    BlackWon,
    WhiteWon,
    Draw
}

public readonly record struct BoardPoint(int Row, int Column);

public readonly record struct Move(Stone Stone, BoardPoint Point);

public sealed record RoomSnapshot(Stone? Turn, MatchOutcome Outcome, IReadOnlyList<Move> Moves)
{
    public const int BoardSize = 15;
}
