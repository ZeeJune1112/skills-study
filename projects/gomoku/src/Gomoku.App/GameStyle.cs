namespace Gomoku.App;

internal static class GameStyle
{
    public static readonly Color Paper = Color.FromArgb(251, 247, 238);
    public static readonly Color Ink = Color.FromArgb(53, 35, 24);
    public static readonly Color Muted = Color.FromArgb(106, 81, 61);
    private static readonly Color Walnut = Color.FromArgb(110, 66, 42);

    public static Button ActionButton(string text, bool primary = false, bool compact = false)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = true,
            MinimumSize = compact ? new(104, 34) : new(156, 38),
            Padding = compact ? new(7, 3, 7, 3) : new(12, 4, 12, 4),
            Margin = compact ? new(0, 0, 6, 6) : new(0, 0, 0, 8),
            FlatStyle = FlatStyle.Flat,
            BackColor = primary ? Walnut : Paper,
            ForeColor = primary ? Color.White : Ink,
            UseVisualStyleBackColor = false
        };
        button.FlatAppearance.BorderColor = Walnut;
        button.FlatAppearance.MouseOverBackColor = primary
            ? Color.FromArgb(134, 82, 53)
            : Color.FromArgb(244, 231, 210);
        return button;
    }
}
