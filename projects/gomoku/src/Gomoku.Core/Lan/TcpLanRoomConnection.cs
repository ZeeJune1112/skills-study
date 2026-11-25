using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;

namespace Gomoku.Core.Lan;

public sealed partial class TcpLanRoomConnection : ILanRoomConnection
{
    private readonly TcpClient client;
    private readonly NetworkStream stream;
    private readonly LanRoomEvent.State initial;
    private readonly IPEndPoint endpoint;
    private readonly string rejoinToken;
    private readonly SemaphoreSlim writeGate = new(1);
    private readonly PeriodicTimer heartbeat = new(TimeSpan.FromSeconds(1));
    private int readerStarted;
    private int disposed;

    public IPEndPoint Endpoint => new(endpoint.Address, endpoint.Port);
    public string RejoinToken => rejoinToken;

    private TcpLanRoomConnection(TcpClient client, NetworkStream stream, LanRoomEvent.State initial,
        IPEndPoint endpoint, string rejoinToken)
    {
        this.client = client;
        this.stream = stream;
        this.initial = initial;
        this.endpoint = endpoint;
        this.rejoinToken = rejoinToken;
        _ = SendHeartbeatsAsync();
    }

    public static Task<TcpLanRoomConnection> ConnectAsync(IPEndPoint endpoint, string roomId,
        CancellationToken cancellationToken = default) =>
        ConnectAsync(endpoint, roomId, null, cancellationToken);

    private static async Task<TcpLanRoomConnection> ConnectAsync(IPEndPoint endpoint, string roomId,
        string? rejoinToken, CancellationToken cancellationToken)
    {
        using var deadline = LanTcpProtocol.Deadline(cancellationToken);
        var client = new TcpClient(AddressFamily.InterNetwork);
        try
        {
            var (stream, token) = await ExchangeJoinAsync(client, endpoint,
                new(LanTcpProtocol.Version, roomId, rejoinToken), deadline.Token).ConfigureAwait(false);
            if (token is null || (rejoinToken is not null && token != rejoinToken))
            {
                throw new InvalidDataException("Invalid LAN join capability.");
            }

            var response = await ReceiveAsync(stream, roomId, deadline.Token).ConfigureAwait(false);
            return response switch
            {
                LanRoomEvent.State state when (state.Snapshot.Seat is null) == (rejoinToken is null) =>
                    new(client, stream, state, endpoint, token),
                LanRoomEvent.Closed => throw new LanRoomException(LanRoomError.RoomNotFound),
                _ => throw new InvalidDataException("Invalid LAN join response.")
            };
        }
        catch (OperationCanceledException error) when (!cancellationToken.IsCancellationRequested)
        {
            client.Dispose();
            throw new TimeoutException("Joining the LAN room timed out.", error);
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    public async ValueTask SendAsync(LanRoomCommand command, CancellationToken cancellationToken = default)
    {
        await writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await LanTcpProtocol.WriteAsync(stream, command, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            Dispose();
            throw;
        }
        finally
        {
            writeGate.Release();
        }
    }

    public async IAsyncEnumerable<LanRoomEvent> ReadEventsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref readerStarted, 1) != 0)
        {
            throw new InvalidOperationException("A LAN connection has one event reader.");
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return initial;
            while (true)
            {
                var message = await ReceiveAsync(stream, initial.Snapshot.RoomId, cancellationToken).ConfigureAwait(false);
                if (message is LanRoomEvent.Heartbeat)
                {
                    continue;
                }

                yield return message;
                if (message is LanRoomEvent.Closed)
                {
                    yield break;
                }
            }
        }
        finally
        {
            Dispose();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 0)
        {
            heartbeat.Dispose();
            client.Dispose();
        }
    }

    private async Task SendHeartbeatsAsync()
    {
        try
        {
            while (await heartbeat.WaitForNextTickAsync().ConfigureAwait(false))
            {
                await SendAsync(new LanRoomCommand.Heartbeat()).ConfigureAwait(false);
            }
        }
        catch (Exception error) when (error is IOException or SocketException or ObjectDisposedException)
        {
            Dispose();
        }
    }

    private static async Task<(NetworkStream Stream, string? RejoinToken)> ExchangeJoinAsync(TcpClient client,
        IPEndPoint endpoint, LanJoinRequest request, CancellationToken cancellationToken)
    {
        client.NoDelay = true;
        await client.ConnectAsync(endpoint, cancellationToken).ConfigureAwait(false);
        var stream = client.GetStream();
        await LanTcpProtocol.WriteAsync(stream, request, cancellationToken).ConfigureAwait(false);
        var response = await LanTcpProtocol.ReadAsync<LanRoomEvent>(stream, cancellationToken).ConfigureAwait(false);
        return response switch
        {
            LanRoomEvent.Rejected rejected when Enum.IsDefined(rejected.Error) => throw new LanRoomException(rejected.Error),
            LanRoomEvent.Joined { RejoinToken: { } token } when !request.Leave && LanTcpProtocol.IsRejoinToken(token) =>
                (stream, token),
            LanRoomEvent.Joined { RejoinToken: null } when request.Leave => (stream, null),
            null => throw new LanRoomException(LanRoomError.ConnectionClosed),
            _ => throw new InvalidDataException("Invalid LAN join response.")
        };
    }

    private static async ValueTask<LanRoomEvent> ReceiveAsync(NetworkStream stream, string roomId,
        CancellationToken cancellationToken)
    {
        LanRoomEvent? message;
        try
        {
            message = await LanTcpProtocol.ReadActiveAsync<LanRoomEvent>(stream, cancellationToken).ConfigureAwait(false);
        }
        catch (IOException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new LanRoomException(LanRoomError.ConnectionClosed);
        }

        return message switch
        {
            null => throw new LanRoomException(LanRoomError.ConnectionClosed),
            LanRoomEvent.Heartbeat => message,
            LanRoomEvent.Closed closed when Enum.IsDefined(closed.Reason) => closed,
            LanRoomEvent.Rejected rejected when Enum.IsDefined(rejected.Error) => rejected,
            LanRoomEvent.Danmaku danmaku when LanRoomCommand.SendDanmaku.IsValidText(danmaku.Text) => danmaku,
            LanRoomEvent.State state when IsValidSnapshot(state.Snapshot, roomId) =>
                new LanRoomEvent.State(state.Snapshot with
                {
                    Board = state.Snapshot.Board with { Moves = Array.AsReadOnly(state.Snapshot.Board.Moves.ToArray()) }
                }),
            _ => throw new InvalidDataException("Invalid LAN room state.")
        };
    }

    private static bool IsValidSnapshot(LanRoomSnapshot snapshot, string roomId) =>
        snapshot.RoomId == roomId && !snapshot.IsHost && Enum.IsDefined(snapshot.Phase)
        && snapshot.SpectatorCapacity is >= LanRoomAuthority.MinSpectatorCapacity and <= LanRoomAuthority.MaxSpectatorCapacity
        && snapshot.SpectatorCount >= 0 && snapshot.SpectatorCount <= snapshot.SpectatorCapacity
        && (snapshot.AiDifficulty is null || Enum.IsDefined(snapshot.AiDifficulty.Value))
        && snapshot.Seat is null or Stone.Black or Stone.White
        && (snapshot.ReconnectWaitSeconds is null or >= LanRoomAuthority.MinReconnectWaitSeconds
            and <= LanRoomAuthority.MaxReconnectWaitSeconds)
        && (snapshot.Phase == LanRoomPhase.Paused
            ? snapshot.ReconnectWait is { Seat: Stone.Black or Stone.White, SecondsRemaining: > 0 } wait &&
                wait.SecondsRemaining <= snapshot.ReconnectWaitSeconds
            : snapshot.ReconnectWait is null)
        && (snapshot.Phase == LanRoomPhase.Aborted
            ? snapshot.AbortReason is { } reason && Enum.IsDefined(reason)
            : snapshot.AbortReason is null)
        && (snapshot.MatchRequest is not { } request ||
            (request.Id != Guid.Empty && Enum.IsDefined(request.Change) && Enum.IsDefined(request.Status)
                && request.Requester is Stone.Black or Stone.White))
        && snapshot.Board.Turn is null or Stone.Black or Stone.White && Enum.IsDefined(snapshot.Board.Outcome)
        && snapshot.Board.Moves.Count <= RoomSnapshot.BoardSize * RoomSnapshot.BoardSize
        && snapshot.Board.Moves.All(move => move.Stone is Stone.Black or Stone.White
            && move.Point.Row is >= 0 and < RoomSnapshot.BoardSize
            && move.Point.Column is >= 0 and < RoomSnapshot.BoardSize);
}
