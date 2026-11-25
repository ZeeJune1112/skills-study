using System.Net;
using Gomoku.Core;
using Gomoku.Core.Lan;

namespace Gomoku.Checks;

internal static partial class Program
{
    [Check]
    private static void LanDuelDeliversDanmakuFromSpectatorsOnly() =>
        CheckDuelDanmakuAsync().GetAwaiter().GetResult();

    private static async Task CheckDuelDanmakuAsync()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var token = deadline.Token;
        await using var room = new TcpLanRoomHost();
        var endpoint = new IPEndPoint(IPAddress.Loopback, room.Port);
        using var white = await TcpLanRoomConnection.ConnectAsync(endpoint, room.RoomId, token);
        using var spectator = await TcpLanRoomConnection.ConnectAsync(endpoint, room.RoomId, token);
        await using var hostEvents = room.Connection.ReadEventsAsync(token).GetAsyncEnumerator();
        await using var whiteEvents = white.ReadEventsAsync(token).GetAsyncEnumerator();
        await using var spectatorEvents = spectator.ReadEventsAsync(token).GetAsyncEnumerator();

        await white.SendAsync(new LanRoomCommand.SelectSeat(Stone.White), token);
        await ExpectRoomAsync(whiteEvents, state => state.Seat == Stone.White);
        await room.Connection.SendAsync(new LanRoomCommand.StartMatch(), token);
        await ExpectRoomAsync(spectatorEvents, state => state.Phase == LanRoomPhase.Playing);

        var seatedMessage = new LanRoomCommand.SendDanmaku("棋手消息");
        await ExpectRejectedAsync(room.Connection, hostEvents, seatedMessage, LanRoomError.DanmakuSpectatorOnly);
        await ExpectRejectedAsync(white, whiteEvents, seatedMessage, LanRoomError.DanmakuSpectatorOnly);
        await spectator.SendAsync(new LanRoomCommand.SendDanmaku("  好棋！  "), token);
        foreach (var recipient in new[] { hostEvents, whiteEvents, spectatorEvents })
        {
            await ExpectDanmakuAsync(recipient, "好棋！");
        }
    }

    [Check]
    private static void LanAiValidatesDanmakuAndDoesNotReplayItOnJoin() =>
        CheckAiDanmakuAsync().GetAwaiter().GetResult();

    private static async Task CheckAiDanmakuAsync()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var token = deadline.Token;
        await using var room = new TcpLanRoomHost(aiDifficulty: AiDifficulty.Easy);
        var endpoint = new IPEndPoint(IPAddress.Loopback, room.Port);
        using var spectator = await TcpLanRoomConnection.ConnectAsync(endpoint, room.RoomId, token);
        await using var hostEvents = room.Connection.ReadEventsAsync(token).GetAsyncEnumerator();
        await using var spectatorEvents = spectator.ReadEventsAsync(token).GetAsyncEnumerator();
        await ExpectRoomAsync(hostEvents, state => state is { Phase: LanRoomPhase.Playing, AiDifficulty: AiDifficulty.Easy });
        await ExpectRejectedAsync(room.Connection, hostEvents, new LanRoomCommand.SendDanmaku("棋手消息"),
            LanRoomError.DanmakuSpectatorOnly);
        foreach (var invalid in new[] { null, "  ", new string('棋', 61), "两行\n消息" })
        {
            await ExpectRejectedAsync(spectator, spectatorEvents, new LanRoomCommand.SendDanmaku(invalid),
                LanRoomError.InvalidDanmaku);
        }

        var longest = new string('棋', 60);
        await spectator.SendAsync(new LanRoomCommand.SendDanmaku(longest), token);
        await ExpectDanmakuAsync(hostEvents, longest);
        await ExpectDanmakuAsync(spectatorEvents, longest);

        using var late = await TcpLanRoomConnection.ConnectAsync(endpoint, room.RoomId, token);
        await using var lateEvents = late.ReadEventsAsync(token).GetAsyncEnumerator();
        Require(await lateEvents.MoveNextAsync() && lateEvents.Current is LanRoomEvent.State
            { Snapshot: { Phase: LanRoomPhase.Playing, Seat: null } }, "A late spectator should start with room state.");
        await spectator.SendAsync(new LanRoomCommand.SendDanmaku("新的弹幕"), token);
        await ExpectDanmakuAsync(lateEvents, "新的弹幕");
    }

    private static async Task ExpectDanmakuAsync(IAsyncEnumerator<LanRoomEvent> events, string expected)
    {
        while (await events.MoveNextAsync())
        {
            if (events.Current is not LanRoomEvent.State)
            {
                Require(events.Current is LanRoomEvent.Danmaku message && message.Text == expected,
                    $"Expected live danmaku '{expected}', got {events.Current}.");
                return;
            }
        }

        throw new InvalidOperationException("The room closed before delivering danmaku.");
    }
}
