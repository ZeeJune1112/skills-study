using Gomoku.Core.Lan;

namespace Gomoku.App;

internal sealed partial class LanMatchView
{
    private readonly TextBox danmakuText = new()
    {
        Anchor = AnchorStyles.Left | AnchorStyles.Right,
        Margin = new(0, 0, 6, 0),
        MaxLength = LanRoomCommand.SendDanmaku.MaxLength,
        PlaceholderText = "输入弹幕",
        AccessibleName = "弹幕内容"
    };

    private readonly Button sendDanmaku = GameStyle.ActionButton("发送弹幕", primary: true, compact: true);
    private readonly TableLayoutPanel danmakuActions = new()
    {
        Dock = DockStyle.Top,
        AutoSize = true,
        ColumnCount = 2,
        Margin = Padding.Empty
    };

    private void AddDanmakuControls(TableLayoutPanel sidebar)
    {
        danmakuActions.ColumnStyles.Add(new(SizeType.Percent, 100));
        danmakuActions.ColumnStyles.Add(new(SizeType.AutoSize));
        sendDanmaku.Margin = Padding.Empty;
        danmakuActions.Controls.Add(danmakuText, 0, 0);
        danmakuActions.Controls.Add(sendDanmaku, 1, 0);
        sidebar.Controls.Add(danmakuActions);
        sendDanmaku.Click += (_, _) => SubmitDanmaku();
        danmakuText.KeyDown += (_, args) =>
        {
            if (args.KeyCode == Keys.Enter)
            {
                args.SuppressKeyPress = true;
                SubmitDanmaku();
            }
        };
    }

    private void RenderDanmakuControls(bool canAct)
    {
        danmakuActions.Visible = connected && snapshot is { Seat: null };
        danmakuActions.Enabled = canAct;
    }

    private bool ReceiveDanmaku(LanRoomEvent message)
    {
        switch (message)
        {
            case LanRoomEvent.Danmaku danmaku:
                board.ShowDanmaku(danmaku.Text);
                return true;
            case LanRoomEvent.Rejected { Error: LanRoomError.DanmakuSpectatorOnly or LanRoomError.InvalidDanmaku } rejected:
                error.Text = LanRoomText.Describe(rejected.Error);
                return true;
            default:
                return false;
        }
    }

    private void SubmitDanmaku()
    {
        if (connected && !pending && snapshot is { Seat: null } && !string.IsNullOrWhiteSpace(danmakuText.Text))
        {
            Send(new LanRoomCommand.SendDanmaku(danmakuText.Text));
            danmakuText.Clear();
        }
    }
}
