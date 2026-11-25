using System.Net.NetworkInformation;
using System.Net.Sockets;
using Gomoku.Core.Lan;

namespace Gomoku.App;

internal static class LanRoomText
{
    public static bool IsNetworkFailure(Exception error) =>
        error is IOException or InvalidDataException or SocketException or NetworkInformationException
            or TimeoutException or ObjectDisposedException;

    public static string Describe(Exception error, string fallback) => error switch
    {
        LanRoomException room => Describe(room.Error),
        InvalidDataException => "收到无效的房间消息。",
        TimeoutException => "连接房间超时，请检查网络和防火墙。",
        _ => fallback
    };

    public static string Describe(LanRoomError error) => error switch
    {
        LanRoomError.RoomNotFound => "房间已关闭或房间标识不匹配。",
        LanRoomError.RoomFull => "观众席已满，等有空席后再试。",
        LanRoomError.ConnectionClosed => "与房主的连接已断开。",
        LanRoomError.RejoinUnavailable => "原对战席已无法恢复。",
        LanRoomError.RejoinExpired => "等待重连超时。",
        LanRoomError.IncompatibleVersion => "双方的程序版本不兼容。",
        LanRoomError.InvalidSeat => "这个对战席无效。",
        LanRoomError.SeatTaken => "该对战席已有人。",
        LanRoomError.NoSeat => "你现在位于观众席。",
        LanRoomError.SeatsLocked => "开局后不能更换对战席。",
        LanRoomError.HostOnly => "只有房主可以执行此操作。",
        LanRoomError.SeatsUnfilled => "黑席和白席均有人后才能开局。",
        LanRoomError.MatchNotPlaying => "当前没有进行中的对局。",
        LanRoomError.ObserverCannotPlay => "观众不能落子。",
        LanRoomError.DanmakuSpectatorOnly => "只有观众可以发送弹幕。",
        LanRoomError.InvalidDanmaku => $"弹幕须为单行，长度为 1–{LanRoomCommand.SendDanmaku.MaxLength}。",
        LanRoomError.NotYourTurn => "还没轮到你落子。",
        LanRoomError.IllegalMove => "只能在棋盘上的空交点落子。",
        LanRoomError.AiActionUnavailable => "当前无法执行此人机操作。",
        LanRoomError.MatchRequestUnavailable => "当前无法请求悔棋或重开。",
        LanRoomError.MatchRequestPending => "已有待确认的请求。",
        LanRoomError.MatchRequestExpired => "该请求已失效。",
        LanRoomError.MatchRequestOpponentOnly => "只有对手可以同意或拒绝此请求。",
        _ => "房间拒绝了此操作。"
    };
}
