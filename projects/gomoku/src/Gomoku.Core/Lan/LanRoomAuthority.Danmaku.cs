namespace Gomoku.Core.Lan;

public sealed partial class LanRoomAuthority
{
    private void SendDanmaku(LocalConnection sender, LanRoomCommand.SendDanmaku command)
    {
        if (SeatOf(sender) is not null)
        {
            sender.Post(new LanRoomEvent.Rejected(LanRoomError.DanmakuSpectatorOnly));
            return;
        }

        var text = command.Text?.Trim();
        if (text is null || !LanRoomCommand.SendDanmaku.IsValidText(text))
        {
            sender.Post(new LanRoomEvent.Rejected(LanRoomError.InvalidDanmaku));
            return;
        }

        var message = new LanRoomEvent.Danmaku(text);
        foreach (var member in members)
        {
            member.PostDanmaku(message);
        }
    }

    private sealed partial class LocalConnection
    {
        public void PostDanmaku(LanRoomEvent.Danmaku message)
        {
            if (events.Reader.Count < 32)
            {
                Post(message);
            }
        }
    }
}
