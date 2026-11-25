using System.Diagnostics;

namespace Gomoku.App;

internal sealed partial class BoardControl
{
    private const int MaxDanmaku = 16;
    private const int DanmakuSpeed = 155;
    private const TextFormatFlags DanmakuTextFlags = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine |
        TextFormatFlags.PreserveGraphicsClipping | TextFormatFlags.NoPadding;
    private readonly Stopwatch danmakuClock = Stopwatch.StartNew();
    private readonly System.Windows.Forms.Timer danmakuTimer = new() { Interval = 33 };
    private readonly double[] danmakuLaneStarts = new double[3];
    private readonly List<(string Text, int Width, int Lane, double Start)> danmakuLines = [];

    public void ShowDanmaku(string text)
    {
        if (danmakuLines.Count >= MaxDanmaku)
        {
            return;
        }

        var width = TextRenderer.MeasureText(text, Font, Size.Empty, DanmakuTextFlags).Width + 12;
        var lane = Array.IndexOf(danmakuLaneStarts, danmakuLaneStarts.Min());
        var start = Math.Max(danmakuClock.Elapsed.TotalSeconds, danmakuLaneStarts[lane]);
        danmakuLaneStarts[lane] = start + (width + 12d) / DanmakuSpeed;
        danmakuLines.Add((text, width, lane, start));
        danmakuTimer.Start();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            danmakuTimer.Dispose();
        }

        base.Dispose(disposing);
    }

    private void DrawDanmaku(Graphics graphics, RectangleF surface)
    {
        if (danmakuLines.Count == 0)
        {
            return;
        }

        var now = danmakuClock.Elapsed.TotalSeconds;
        danmakuLines.RemoveAll(line => (now - line.Start) * DanmakuSpeed >= surface.Width + line.Width);
        if (danmakuLines.Count == 0)
        {
            danmakuTimer.Stop();
            return;
        }

        var clip = graphics.Save();
        graphics.IntersectClip(surface);
        using var background = new SolidBrush(Color.FromArgb(220, GameStyle.Ink));
        var height = Font.Height + 6;
        foreach (var line in danmakuLines)
        {
            if (line.Start <= now)
            {
                var left = (int)(surface.Right - (now - line.Start) * DanmakuSpeed);
                var top = (int)surface.Top + 5 + line.Lane * (height + 4);
                graphics.FillRectangle(background, left, top, line.Width, height);
                TextRenderer.DrawText(graphics, line.Text, Font, new Point(left + 6, top + 3),
                    Color.White, DanmakuTextFlags);
            }
        }

        graphics.Restore(clip);
    }
}
