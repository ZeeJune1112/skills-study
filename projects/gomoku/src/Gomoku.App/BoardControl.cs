using System.Drawing.Drawing2D;
using Gomoku.Core;

namespace Gomoku.App;

internal sealed partial class BoardControl : Control
{
    private const int SizeInPoints = RoomSnapshot.BoardSize;
    private static readonly BoardPoint[] StarPoints = [new(3, 3), new(3, 11), new(7, 7), new(11, 3), new(11, 11)];
    private static readonly Color Walnut = Color.FromArgb(100, 62, 35);
    private RoomSnapshot? position;
    private Stone? inputStone;
    private BoardPoint? hoveredPoint;
    private BoardPoint keyboardPoint = new(7, 7);
    private bool keyboardInput;

    public event Action<BoardPoint>? PointSelected;

    public BoardControl()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        TabStop = false;
        AccessibleName = "五子棋棋盘";
        AccessibleDescription = "15×15 棋盘。";
        danmakuTimer.Tick += (_, _) => Invalidate();
    }

    public void SetPosition(RoomSnapshot snapshot, Stone? localStone, bool canPlace)
    {
        position = snapshot;
        inputStone = canPlace ? localStone : null;
        TabStop = localStone.HasValue;
        AccessibleDescription = TabStop ? "方向键选择交点，按空格或回车落子。" : "15×15 棋盘，仅供观战。";
        RefreshPointer();
    }

    protected override void OnPaint(PaintEventArgs args)
    {
        base.OnPaint(args);
        var graphics = args.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var (surface, origin, spacing) = Geometry();
        using var wood = new LinearGradientBrush(surface, Color.FromArgb(251, 224, 178),
            Color.FromArgb(229, 187, 129), LinearGradientMode.ForwardDiagonal);
        using var grid = new Pen(Walnut, Math.Max(1, spacing * 0.036f));
        using var star = new SolidBrush(Walnut);
        graphics.FillRectangle(wood, surface);
        graphics.DrawRectangle(grid, surface.X, surface.Y, surface.Width, surface.Height);

        var last = Intersection(new(SizeInPoints - 1, SizeInPoints - 1), origin, spacing);
        using var coordinates = new Font(Font.FontFamily, Math.Clamp(spacing * 0.38f, 7, 11), GraphicsUnit.Pixel);
        const TextFormatFlags labelFormat = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
            TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;
        var gutter = origin.X - surface.X;
        for (var index = 0; index < SizeInPoints; index++)
        {
            var offset = index * spacing;
            graphics.DrawLine(grid, origin.X, origin.Y + offset, last.X, origin.Y + offset);
            graphics.DrawLine(grid, origin.X + offset, origin.Y, origin.X + offset, last.Y);
            var columnLabel = new RectangleF(origin.X + offset - spacing / 2, surface.Y, spacing, gutter);
            var rowLabel = new RectangleF(surface.X, origin.Y + offset - spacing / 2, gutter, spacing);
            TextRenderer.DrawText(graphics, ((char)('A' + index)).ToString(), coordinates,
                Rectangle.Round(columnLabel), Walnut, labelFormat);
            TextRenderer.DrawText(graphics, (index + 1).ToString(), coordinates, Rectangle.Round(rowLabel), Walnut, labelFormat);
        }

        foreach (var point in StarPoints)
        {
            graphics.FillEllipse(star, Circle(Intersection(point, origin, spacing), spacing * 0.085f));
        }

        if (position is { Moves.Count: > 0 } snapshot)
        {
            foreach (var move in snapshot.Moves)
            {
                DrawStone(graphics, Intersection(move.Point, origin, spacing), spacing * 0.405f, move.Stone);
            }

            var latest = snapshot.Moves[^1];
            using var marker = new SolidBrush(latest.Stone == Stone.Black ? Color.Wheat : Walnut);
            graphics.FillEllipse(marker, Circle(Intersection(latest.Point, origin, spacing), spacing * 0.09f));
        }

        var showKeyboard = TabStop && keyboardInput && Focused;
        var selected = showKeyboard ? keyboardPoint : hoveredPoint;
        if (selected is { } target && inputStone is { } stone && CanPlay(target))
        {
            DrawStone(graphics, Intersection(target, origin, spacing), spacing * 0.405f, stone, 105);
        }

        DrawDanmaku(graphics, surface);
        if (showKeyboard)
        {
            using var focus = new Pen(Walnut, 2) { DashStyle = DashStyle.Dot };
            var outline = Circle(Intersection(keyboardPoint, origin, spacing), spacing * 0.47f);
            graphics.DrawRectangle(focus, outline.X, outline.Y, outline.Width, outline.Height);
        }
    }

    protected override void OnMouseMove(MouseEventArgs args)
    {
        base.OnMouseMove(args);
        var point = PointAt(args.Location);
        if (hoveredPoint != point || keyboardInput)
        {
            hoveredPoint = point;
            keyboardInput = false;
            RefreshPointer();
        }
    }

    protected override void OnMouseLeave(EventArgs args)
    {
        base.OnMouseLeave(args);
        hoveredPoint = null;
        RefreshPointer();
    }

    protected override void OnMouseDown(MouseEventArgs args)
    {
        base.OnMouseDown(args);
        if (TabStop && args.Button == MouseButtons.Left)
        {
            Focus();
            keyboardInput = false;
            hoveredPoint = PointAt(args.Location);
            RefreshPointer();
            if (hoveredPoint is { } point && CanPlay(point))
            {
                keyboardPoint = point;
                PointSelected?.Invoke(point);
            }
        }
    }

    protected override bool IsInputKey(Keys keyData) =>
        (TabStop && (keyData & Keys.KeyCode) is Keys.Up or Keys.Down or Keys.Left or Keys.Right or Keys.Enter or Keys.Space)
        || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs args)
    {
        base.OnKeyDown(args);
        if (!TabStop)
        {
            return;
        }

        var next = args.KeyCode switch
        {
            Keys.Up => keyboardPoint with { Row = Math.Max(0, keyboardPoint.Row - 1) },
            Keys.Down => keyboardPoint with { Row = Math.Min(SizeInPoints - 1, keyboardPoint.Row + 1) },
            Keys.Left => keyboardPoint with { Column = Math.Max(0, keyboardPoint.Column - 1) },
            Keys.Right => keyboardPoint with { Column = Math.Min(SizeInPoints - 1, keyboardPoint.Column + 1) },
            Keys.Enter or Keys.Space => keyboardPoint,
            _ => (BoardPoint?)null
        };
        if (next is { } point)
        {
            keyboardPoint = point;
            keyboardInput = true;
            args.SuppressKeyPress = true;
            Invalidate();
            if (args.KeyCode is Keys.Enter or Keys.Space && CanPlay(point))
            {
                PointSelected?.Invoke(point);
            }
        }
    }

    protected override void OnGotFocus(EventArgs args)
    {
        base.OnGotFocus(args);
        keyboardInput = true;
        Invalidate();
    }

    protected override void OnLostFocus(EventArgs args)
    {
        base.OnLostFocus(args);
        Invalidate();
    }

    private bool CanPlay(BoardPoint point) =>
        inputStone is { } stone && position is { } snapshot && snapshot.Turn == stone &&
        !snapshot.Moves.Any(move => move.Point == point);

    private void RefreshPointer()
    {
        Cursor = hoveredPoint is { } point && CanPlay(point) ? Cursors.Hand : Cursors.Default;
        Invalidate();
    }

    private BoardPoint? PointAt(Point pointer)
    {
        var (_, origin, spacing) = Geometry();
        var row = (pointer.Y - origin.Y) / spacing;
        var column = (pointer.X - origin.X) / spacing;
        var nearest = new BoardPoint((int)MathF.Round(row), (int)MathF.Round(column));
        return nearest.Row is >= 0 and < SizeInPoints && nearest.Column is >= 0 and < SizeInPoints
            && Math.Abs(row - nearest.Row) <= 0.45f && Math.Abs(column - nearest.Column) <= 0.45f
            ? nearest
            : null;
    }

    private (RectangleF Surface, PointF Origin, float Spacing) Geometry()
    {
        var side = Math.Max(1, Math.Min(ClientSize.Width, ClientSize.Height) - 8);
        var surface = new RectangleF((ClientSize.Width - side) / 2f, (ClientSize.Height - side) / 2f, side, side);
        var margin = side * 0.055f;
        return (surface, new(surface.X + margin, surface.Y + margin), (side - 2 * margin) / (SizeInPoints - 1));
    }

    private static PointF Intersection(BoardPoint point, PointF origin, float spacing) =>
        new(origin.X + point.Column * spacing, origin.Y + point.Row * spacing);

    private static RectangleF Circle(PointF center, float radius) =>
        new(center.X - radius, center.Y - radius, 2 * radius, 2 * radius);

    internal static void DrawStone(Graphics graphics, PointF center, float radius, Stone stone, int opacity = 255)
    {
        var dark = stone == Stone.Black;
        var highlight = dark ? Color.FromArgb(83, 84, 76) : Color.White;
        var shade = dark ? Color.FromArgb(25, 25, 21) : Color.FromArgb(235, 225, 205);
        var outline = dark ? Color.FromArgb(19, 20, 17) : Color.FromArgb(156, 141, 120);
        var circle = Circle(center, radius);
        using var brush = new LinearGradientBrush(circle, Color.FromArgb(opacity, highlight),
            Color.FromArgb(opacity, shade), LinearGradientMode.ForwardDiagonal);
        using var border = new Pen(Color.FromArgb(opacity, outline));
        graphics.FillEllipse(brush, circle);
        graphics.DrawEllipse(border, circle);
    }
}
