using System.Drawing.Drawing2D;
using Gomoku.Core;
using Gomoku.Core.Lan;

namespace Gomoku.App;

internal sealed partial class LanMatchView
{
    private static readonly Color SeatSurface = Color.FromArgb(255, 250, 241);
    private static readonly Color CurrentSeatSurface = Color.FromArgb(227, 238, 229);
    private static readonly Color SpectatorInk = Color.FromArgb(35, 87, 70);
    private static readonly Color FrameBorder = Color.FromArgb(221, 204, 182);
    private readonly Font roomTitleFont = new("Microsoft YaHei UI", 15, FontStyle.Bold);
    private readonly Font roleTitleFont = new("Microsoft YaHei UI", 15, FontStyle.Bold);
    private readonly Label title = new() { AutoSize = true, Anchor = AnchorStyles.Left };
    private readonly Label roomId = new()
    {
        Anchor = AnchorStyles.Left | AnchorStyles.Right,
        AutoEllipsis = true,
        ForeColor = GameStyle.Muted,
        Height = 26,
        TextAlign = ContentAlignment.MiddleLeft
    };
    private readonly TableLayoutPanel roleCard = new();
    private readonly RoleMark roleMark = new(42) { Margin = new(0, 0, 12, 0), Anchor = AnchorStyles.Left };
    private readonly Label roleTitle = new() { AutoSize = true, Margin = Padding.Empty };
    private readonly Label status = new() { AutoSize = true, MaximumSize = new(175, 0), Margin = new(0, 3, 0, 0) };
    private readonly Label error = new() { AutoSize = true, MaximumSize = new(230, 0), ForeColor = Color.Firebrick };
    private readonly Label blackSeat = new() { AutoSize = true, Anchor = AnchorStyles.Left };
    private readonly Label whiteSeat = new() { AutoSize = true, Anchor = AnchorStyles.Left };
    private readonly Label spectatorSeat = new() { AutoSize = true, Anchor = AnchorStyles.Left };
    private readonly Label blackSeatStatus = SeatStatusLabel();
    private readonly Label whiteSeatStatus = SeatStatusLabel();
    private readonly Label spectators = SeatStatusLabel();

    private Control CreateRoomLayout()
    {
        title.Font = roomTitleFont;
        exit.Anchor = AnchorStyles.Right;
        exit.Margin = Padding.Empty;
        var header = new TableLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            Margin = Padding.Empty,
            Padding = new(15, 7, 15, 7),
            BackColor = SeatSurface
        };
        header.ColumnStyles.Add(new(SizeType.AutoSize));
        header.ColumnStyles.Add(new(SizeType.Percent, 100));
        header.ColumnStyles.Add(new(SizeType.AutoSize));
        header.Controls.Add(title, 0, 0);
        header.Controls.Add(roomId, 1, 0);
        header.Controls.Add(exit, 2, 0);
        Frame(header);

        var boardFrame = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(239, 227, 210),
            Margin = Padding.Empty,
            Padding = new(8),
            TabIndex = 0
        };
        boardFrame.Controls.Add(board);
        Frame(boardFrame);

        var sidebar = CreateSidebar();
        var viewport = new Panel { AutoScroll = true, Dock = DockStyle.Fill, Margin = new(14, 0, 0, 0), TabIndex = 1 };
        viewport.Controls.Add(sidebar);
        viewport.SizeChanged += (_, _) => CenterSidebar();
        sidebar.SizeChanged += (_, _) => CenterSidebar();

        void CenterSidebar()
        {
            var top = Math.Max(0, (viewport.ClientSize.Height - sidebar.Height) / 2);
            if (viewport.Padding.Top != top)
            {
                viewport.Padding = new(0, top, 0, 0);
            }
        }

        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new(14),
            Margin = Padding.Empty,
            ColumnCount = 2,
            RowCount = 1
        };
        body.ColumnStyles.Add(new(SizeType.Percent, 100));
        body.ColumnStyles.Add(new(SizeType.Absolute, 268));
        body.RowStyles.Add(new(SizeType.Percent, 100));
        body.Controls.Add(boardFrame, 0, 0);
        body.Controls.Add(viewport, 1, 0);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        layout.ColumnStyles.Add(new(SizeType.Percent, 100));
        layout.RowStyles.Add(new(SizeType.AutoSize));
        layout.RowStyles.Add(new(SizeType.Percent, 100));
        layout.Controls.Add(header, 0, 0);
        layout.Controls.Add(body, 0, 1);
        return layout;
    }

    private TableLayoutPanel CreateSidebar()
    {
        roleTitle.Font = roleTitleFont;
        roleCard.AutoSize = true;
        roleCard.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        roleCard.Dock = DockStyle.Top;
        roleCard.ColumnCount = 2;
        roleCard.RowCount = 2;
        roleCard.Padding = new(13, 12, 10, 12);
        roleCard.Margin = new(0, 0, 0, 10);
        roleCard.ColumnStyles.Add(new(SizeType.AutoSize));
        roleCard.ColumnStyles.Add(new(SizeType.Percent, 100));
        roleCard.Controls.Add(roleMark, 0, 0);
        roleCard.SetRowSpan(roleMark, 2);
        roleCard.Controls.Add(roleTitle, 1, 0);
        roleCard.Controls.Add(status, 1, 1);
        Frame(roleCard);

        error.Visible = false;
        error.TextChanged += (_, _) => error.Visible = error.Text.Length > 0;
        var sidebar = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Margin = Padding.Empty
        };
        sidebar.ColumnStyles.Add(new(SizeType.Percent, 100));
        sidebar.Controls.Add(roleCard);
        sidebar.Controls.Add(CreateSeatList());
        sidebar.Controls.Add(error);
        sidebar.Controls.Add(ActionRow(black, white, start, withdraw));
        sidebar.Controls.Add(difficultyLabel);
        sidebar.Controls.Add(difficultyPicker);
        sidebar.Controls.Add(ActionRow(undoAi, restartAi));
        AddMatchRequestControls(sidebar);
        AddDanmakuControls(sidebar);
        return sidebar;
    }

    private TableLayoutPanel CreateSeatList()
    {
        var list = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Top,
            BackColor = SeatSurface,
            ColumnCount = 3,
            RowCount = 3,
            Padding = new(9, 5, 9, 5),
            Margin = new(0, 0, 0, 10)
        };
        list.ColumnStyles.Add(new(SizeType.AutoSize));
        list.ColumnStyles.Add(new(SizeType.Percent, 100));
        list.ColumnStyles.Add(new(SizeType.AutoSize));
        list.Controls.Add(new RoleMark(15, Stone.Black), 0, 0);
        list.Controls.Add(blackSeat, 1, 0);
        list.Controls.Add(blackSeatStatus, 2, 0);
        list.Controls.Add(new RoleMark(15, Stone.White), 0, 1);
        list.Controls.Add(whiteSeat, 1, 1);
        list.Controls.Add(whiteSeatStatus, 2, 1);
        list.Controls.Add(new RoleMark(15), 0, 2);
        list.Controls.Add(spectatorSeat, 1, 2);
        list.Controls.Add(spectators, 2, 2);
        Frame(list);
        return list;
    }

    private void RenderRoomDetails()
    {
        var seat = snapshot?.Seat;
        var isAi = snapshot?.AiDifficulty.HasValue == true;
        title.Text = isAi ? "人机对战" : "双人对战";
        roomId.Text = snapshot is { } state ? $"房间标识  {state.RoomId}" : "";
        (roleCard.BackColor, roleCard.ForeColor, roleTitle.Text) = seat switch
        {
            Stone.Black => (Color.FromArgb(43, 44, 37), Color.FromArgb(255, 246, 230), "你执黑"),
            Stone.White => (Color.FromArgb(255, 253, 246), GameStyle.Ink, "你执白"),
            _ => (Color.FromArgb(226, 238, 229), SpectatorInk,
                snapshot is null ? "连接房间" : "你在观众席")
        };
        roleMark.SetSeat(seat);
        status.Text = reconnectAttempt is not null ? "连接中断，正在重新连接…" :
            connected ? PositionText() : "连接已结束";
        blackSeat.Text = SeatName("黑棋", Stone.Black);
        whiteSeat.Text = isAi ? "白棋 · 电脑" : SeatName("白棋", Stone.White);
        spectatorSeat.Text = seat is null && snapshot?.IsHost == true ? "观众席 · 房主" : "观众席";
        spectators.Text = snapshot is { } room ? $"{room.SpectatorCount}/{room.SpectatorCapacity}" : "—";
        RenderSeatStatus(blackSeatStatus, Stone.Black, snapshot?.BlackOccupied == true);
        RenderSeatStatus(whiteSeatStatus, Stone.White, snapshot?.WhiteOccupied == true);
    }

    private string SeatName(string name, Stone seat) =>
        snapshot?.Seat != seat ? name : snapshot.IsHost ? $"{name} · 房主" : $"{name} · 你";

    private void RenderSeatStatus(Label label, Stone seat, bool occupied)
    {
        var isCurrent = occupied && snapshot is { Phase: LanRoomPhase.Playing } state && state.Board.Turn == seat;
        label.BackColor = isCurrent ? CurrentSeatSurface : SeatSurface;
        label.ForeColor = isCurrent ? SpectatorInk : GameStyle.Muted;
        label.Text = !occupied ? "空席" : snapshot?.Phase switch
        {
            LanRoomPhase.Playing => isCurrent ? "本手" : "等待",
            LanRoomPhase.Paused when snapshot.ReconnectWait?.Seat == seat => "断线",
            LanRoomPhase.Paused => "暂停",
            LanRoomPhase.Finished => "已结束",
            _ => "已就位"
        };
    }

    private static FlowLayoutPanel ActionRow(params Button[] buttons)
    {
        var row = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = Padding.Empty
        };
        row.Controls.AddRange(buttons);
        return row;
    }

    private static Label SeatStatusLabel() => new()
    {
        AutoSize = true,
        Anchor = AnchorStyles.Right,
        ForeColor = GameStyle.Muted,
        Margin = new(0, 1, 0, 1),
        Padding = new(6, 2, 6, 2)
    };

    private static void Frame(Control control)
    {
        control.Paint += (_, args) =>
        {
            using var border = new Pen(FrameBorder);
            args.Graphics.DrawRectangle(border, 0, 0, control.ClientSize.Width - 1, control.ClientSize.Height - 1);
        };
    }

    private sealed class RoleMark : Control
    {
        private Stone? seat;

        public RoleMark(int size, Stone? seat = null)
        {
            this.seat = seat;
            SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Size = new(size, size);
            Anchor = AnchorStyles.Left;
            TabStop = false;
        }

        public void SetSeat(Stone? value)
        {
            if (seat != value)
            {
                seat = value;
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs args)
        {
            base.OnPaint(args);
            var graphics = args.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var center = new PointF(Width / 2f, Height / 2f);
            var radius = Math.Min(Width, Height) * 0.44f;
            if (seat is { } stone)
            {
                BoardControl.DrawStone(graphics, center, radius, stone);
                return;
            }

            var left = new PointF(center.X - radius, center.Y);
            var right = new PointF(center.X + radius, center.Y);
            using var outline = new GraphicsPath();
            outline.AddBezier(left, new(center.X - radius / 3, center.Y - radius),
                new(center.X + radius / 3, center.Y - radius), right);
            outline.AddBezier(right, new(center.X + radius / 3, center.Y + radius),
                new(center.X - radius / 3, center.Y + radius), left);
            using var pen = new Pen(SpectatorInk, Math.Max(1, radius * 0.12f));
            using var pupil = new SolidBrush(SpectatorInk);
            graphics.DrawPath(pen, outline);
            graphics.FillEllipse(pupil, center.X - radius / 4, center.Y - radius / 4, radius / 2, radius / 2);
        }
    }
}
