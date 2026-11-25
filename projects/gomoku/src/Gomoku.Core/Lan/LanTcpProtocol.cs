using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Gomoku.Core.Lan;

internal sealed record LanJoinRequest(int Version, string RoomId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] string? RejoinToken = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Leave = false);

internal static class LanTcpProtocol
{
    public const int Version = 5;
    public static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(5);
    private const int MaxFrameBytes = 16 * 1024;
    private static readonly TimeSpan IoTimeout = TimeSpan.FromSeconds(5);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        MaxDepth = 12,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static bool IsRejoinToken(string? token) =>
        token is { Length: LanRoomAuthority.RejoinTokenLength } && token.All(char.IsAsciiHexDigitUpper);

    public static CancellationTokenSource Deadline(CancellationToken cancellationToken)
    {
        var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(IoTimeout);
        return deadline;
    }

    public static async ValueTask<T?> ReadActiveAsync<T>(NetworkStream stream, CancellationToken cancellationToken)
        where T : class
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(IdleTimeout);
        try
        {
            return await ReadAsync<T>(stream, deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException error) when (!cancellationToken.IsCancellationRequested)
        {
            throw new IOException("LAN connection stopped responding.", error);
        }
    }

    public static async ValueTask<T?> ReadAsync<T>(NetworkStream stream, CancellationToken cancellationToken)
        where T : class
    {
        var header = new byte[sizeof(int)];
        if (await stream.ReadAsync(header.AsMemory(0, 1), cancellationToken).ConfigureAwait(false) == 0)
        {
            return null;
        }

        using var deadline = Deadline(cancellationToken);
        try
        {
            await stream.ReadExactlyAsync(header.AsMemory(1), deadline.Token).ConfigureAwait(false);
            var size = BinaryPrimitives.ReadInt32BigEndian(header);
            if (size is <= 0 or > MaxFrameBytes)
            {
                throw new InvalidDataException("Invalid LAN message length.");
            }

            var payload = new byte[size];
            await stream.ReadExactlyAsync(payload, deadline.Token).ConfigureAwait(false);
            return JsonSerializer.Deserialize<T>(payload, JsonOptions)
                ?? throw new InvalidDataException("Missing LAN message.");
        }
        catch (Exception error) when (error is JsonException or NotSupportedException)
        {
            throw new InvalidDataException("Invalid LAN message.", error);
        }
        catch (OperationCanceledException error) when (!cancellationToken.IsCancellationRequested)
        {
            throw new IOException("Incomplete LAN message.", error);
        }
    }

    public static async ValueTask WriteAsync<T>(NetworkStream stream, T message, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(message, JsonOptions);
        if (payload.Length > MaxFrameBytes)
        {
            throw new InvalidDataException("LAN message exceeds the maximum length.");
        }

        var frame = new byte[sizeof(int) + payload.Length];
        BinaryPrimitives.WriteInt32BigEndian(frame, payload.Length);
        payload.CopyTo(frame.AsSpan(sizeof(int)));
        using var deadline = Deadline(cancellationToken);
        try
        {
            await stream.WriteAsync(frame, deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException error) when (!cancellationToken.IsCancellationRequested)
        {
            throw new IOException("Sending a LAN message timed out.", error);
        }
    }
}
