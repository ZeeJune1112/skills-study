using Gomoku.Core.Lan;

namespace Gomoku.App;

internal sealed class MainForm : Form
{
    private bool closingRoom;
    private bool canClose;

    public MainForm()
    {
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new("Microsoft YaHei UI", 10);
        BackColor = GameStyle.Paper;
        ForeColor = GameStyle.Ink;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new(740, 540);
        MinimumSize = new(630, 460);
        ShowLobby();
    }

    private void ShowLanMatch(ILanRoomConnection connection, TcpLanRoomHost? host)
    {
        Text = "五子棋 · 局域网对战";
        var match = new LanMatchView(connection, host) { Dock = DockStyle.Fill };
        match.ExitRequested += ShowLobby;
        ShowScreen(match);
        match.SelectBoard();
    }

    private void ShowLobby(string? notice = null)
    {
        Text = "五子棋 · 大厅";
        var lobby = new LanLobbyView(notice) { Dock = DockStyle.Fill };
        lobby.RoomEntered += ShowLanMatch;
        ShowScreen(lobby);
        AcceptButton = lobby.AiButton;
        lobby.AiButton.Select();
    }

    protected override async void OnFormClosing(FormClosingEventArgs args)
    {
        base.OnFormClosing(args);
        if (args.Cancel || canClose || Controls.Count == 0 || Controls[0] is not LanMatchView match)
        {
            return;
        }

        args.Cancel = true;
        if (closingRoom)
        {
            return;
        }

        closingRoom = true;
        await match.LeaveRoomAsync();
        canClose = true;
        BeginInvoke(Close);
    }

    private void ShowScreen(Control screen)
    {
        SuspendLayout();
        AcceptButton = null;
        if (Controls.Count > 0)
        {
            Controls[0].Dispose();
        }

        Controls.Add(screen);
        ResumeLayout();
    }
}
