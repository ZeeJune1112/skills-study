using System.Text.Json.Serialization;

namespace Gomoku.Core.Lan;

[JsonDerivedType(typeof(LanRoomCommand.SendDanmaku), "sendDanmaku")]
public abstract partial record LanRoomCommand
{
    public sealed record SendDanmaku(string? Text) : LanRoomCommand
    {
        public const int MaxLength = 60;

        internal static bool IsValidText(string text) =>
            text.Length is > 0 and <= MaxLength && !string.IsNullOrWhiteSpace(text) &&
            !text.Any(character => char.IsControl(character) || character is '\u2028' or '\u2029');
    }
}

[JsonDerivedType(typeof(LanRoomEvent.Danmaku), "danmaku")]
public abstract partial record LanRoomEvent
{
    public sealed record Danmaku(string Text) : LanRoomEvent;
}
