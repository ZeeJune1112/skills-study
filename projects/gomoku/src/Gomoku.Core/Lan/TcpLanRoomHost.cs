using System.Net;
using System.Net.Sockets;

namespace Gomoku.Core.Lan;

public sealed class TcpLanRoomHost : IAsyncDisposable
{
    private const int MaxConnections = 40;
    private readonly TcpListener listener = new(IPAddress.Any, 0);
    private readonly LanRoomAuthority room;
    private readonly CancellationTokenSource stopping = new();
    private readonly object disposalGate = new();
    private Task? disposal;

    public string RoomId => room.RoomId;
    public bool IsAi => room.IsAi;
    public int Port { get; }
    public ILanRoomConnection Connection => room.Host;
    public Task Completion { get; }

    public TcpLanRoomHost(int spectatorCapacity = LanRoomAuthority.DefaultSpectatorCapacity,
        AiDifficulty? aiDifficulty = null, int? reconnectWaitSeconds = null, TimeProvider? timeProvider = null)
    {
        room = new LanRoomAuthority(spectatorCapacity, aiDifficulty, reconnectWaitSeconds, timeProvider);
        try
        {
            listener.Start(MaxConnections);
            Port = ((IPEndPoint)listener.LocalEndpoint).Port;
            Completion = RunAsync(stopping.Token);
        }
        catch
        {
            listener.Stop();
            room.Dispose();
            stopping.Dispose();
            throw;
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (disposalGate)
        {
            return new(disposal ??= StopAsync());
        }
    }

    private async Task StopAsync()
    {
        try
        {
            await stopping.CancelAsync().ConfigureAwait(false);
            await Completion.ConfigureAwait(false);
        }
        finally
        {
            stopping.Dispose();
        }
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        using var handlersStopping = new CancellationTokenSource();
        var handlers = new List<Task>();
        try
        {
            while (true)
            {
                var client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                handlers.RemoveAll(handler => handler.IsCompletedSuccessfully);
                if (handlers.Count >= MaxConnections)
                {
                    client.Dispose();
                    continue;
                }

                handlers.Add(HandleClientAsync(client, handlersStopping.Token));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            await stopping.CancelAsync().ConfigureAwait(false);
            listener.Stop();
            room.Dispose();
            handlersStopping.CancelAfter(TimeSpan.FromSeconds(2));
            await Task.WhenAll(handlers).ConfigureAwait(false);
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using (client)
        {
            ILanRoomGuestConnection? member = null;
            try
            {
                client.NoDelay = true;
                using var stream = client.GetStream();
                member = await JoinAsync(stream, cancellationToken).ConfigureAwait(false);
                if (member is null)
                {
                    return;
                }

                using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                var receive = ReceiveCommandsAsync(stream, member, lifetime.Token);
                var send = SendEventsAsync(stream, member, lifetime.Token);
                await Task.WhenAny(receive, send).ConfigureAwait(false);
                await lifetime.CancelAsync().ConfigureAwait(false);
                await Task.WhenAll(receive, send).ConfigureAwait(false);
            }
            catch (Exception error) when (error is IOException or SocketException or InvalidDataException
                or OperationCanceledException or ObjectDisposedException)
            {
            }
            finally
            {
                member?.Disconnect();
            }
        }
    }

    private async Task<ILanRoomGuestConnection?> JoinAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        using var deadline = LanTcpProtocol.Deadline(cancellationToken);
        var request = await LanTcpProtocol.ReadAsync<LanJoinRequest>(stream, deadline.Token).ConfigureAwait(false);
        if (request is null)
        {
            return null;
        }

        ILanRoomGuestConnection? member = null;
        LanRoomEvent response;
        try
        {
            if (request.Version != LanTcpProtocol.Version)
            {
                throw new LanRoomException(LanRoomError.IncompatibleVersion);
            }

            if (request.RejoinToken is { } token && !LanTcpProtocol.IsRejoinToken(token))
            {
                throw new LanRoomException(LanRoomError.RejoinUnavailable);
            }

            if (request.Leave)
            {
                room.LeaveGuest(request.RoomId, request.RejoinToken ?? "");
            }
            else
            {
                member = room.Join(request.RoomId, request.RejoinToken);
            }

            response = new LanRoomEvent.Joined(member?.RejoinToken);
        }
        catch (LanRoomException error)
        {
            response = new LanRoomEvent.Rejected(error.Error);
        }

        try
        {
            await LanTcpProtocol.WriteAsync(stream, response, deadline.Token).ConfigureAwait(false);
            return member;
        }
        catch
        {
            member?.Disconnect();
            throw;
        }
    }

    private static async Task ReceiveCommandsAsync(NetworkStream stream, ILanRoomConnection member,
        CancellationToken cancellationToken)
    {
        while (await LanTcpProtocol.ReadActiveAsync<LanRoomCommand>(stream, cancellationToken).ConfigureAwait(false)
            is { } command)
        {
            await member.SendAsync(command, cancellationToken).ConfigureAwait(false);
            if (command is LanRoomCommand.LeaveRoom)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static async Task SendEventsAsync(NetworkStream stream, ILanRoomConnection member,
        CancellationToken cancellationToken)
    {
        await foreach (var message in member.ReadEventsAsync(cancellationToken).ConfigureAwait(false))
        {
            await LanTcpProtocol.WriteAsync(stream, message, cancellationToken).ConfigureAwait(false);
        }
    }
}
