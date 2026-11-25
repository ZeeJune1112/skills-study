using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Gomoku.Core.Lan;

public sealed partial class TcpLanRoomConnection
{
    public static async Task<TcpLanRoomConnection> RejoinAsync(IPEndPoint endpoint, string roomId, string rejoinToken,
        CancellationToken cancellationToken = default)
    {
        if (!LanTcpProtocol.IsRejoinToken(rejoinToken))
        {
            throw new LanRoomException(LanRoomError.RejoinUnavailable);
        }

        var started = Environment.TickCount64;
        while (true)
        {
            try
            {
                return await ConnectAsync(endpoint, roomId, rejoinToken, cancellationToken).ConfigureAwait(false);
            }
            catch (LanRoomException error) when (error.Error == LanRoomError.RejoinPending &&
                Environment.TickCount64 - started < LanTcpProtocol.IdleTimeout.TotalMilliseconds)
            {
                await Task.Delay(200, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public async Task<TcpLanRoomConnection> RejoinAsync(CancellationToken cancellationToken = default,
        int discoveryPort = LanDiscovery.DefaultPort)
    {
        if (initial.Snapshot.ReconnectWaitSeconds is not { } seconds)
        {
            throw new LanRoomException(LanRoomError.RejoinUnavailable);
        }

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(seconds) + LanTcpProtocol.IdleTimeout);
        try
        {
            return await RetryEndpointAsync(
                (candidate, token) => ConnectAsync(candidate, initial.Snapshot.RoomId, rejoinToken, token),
                discoveryPort, budget.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new LanRoomException(LanRoomError.RejoinExpired);
        }
    }

    public async Task NotifyDepartureAsync(CancellationToken cancellationToken)
    {
        await RetryEndpointAsync(async (candidate, token) =>
        {
            using var departure = new TcpClient(AddressFamily.InterNetwork);
            await ExchangeJoinAsync(departure, candidate,
                new(LanTcpProtocol.Version, initial.Snapshot.RoomId, rejoinToken, Leave: true), token)
                .ConfigureAwait(false);
            return true;
        }, LanDiscovery.DefaultPort, cancellationToken).ConfigureAwait(false);
    }

    private async Task<T> RetryEndpointAsync<T>(Func<IPEndPoint, CancellationToken, Task<T>> attempt,
        int discoveryPort, CancellationToken cancellationToken)
    {
        var candidate = endpoint;
        while (true)
        {
            using var connectionAttempt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            connectionAttempt.CancelAfter(TimeSpan.FromSeconds(1));
            try
            {
                return await attempt(candidate, connectionAttempt.Token).ConfigureAwait(false);
            }
            catch (LanRoomException error) when (error.Error is
                LanRoomError.RoomNotFound or LanRoomError.RejoinPending or LanRoomError.ConnectionClosed)
            {
            }
            catch (Exception error) when (error is SocketException or TimeoutException or OperationCanceledException
                or (IOException and not LanRoomException))
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            candidate = await DiscoverEndpointAsync(discoveryPort, cancellationToken).ConfigureAwait(false) ?? endpoint;
        }
    }

    private async Task<IPEndPoint?> DiscoverEndpointAsync(int discoveryPort, CancellationToken cancellationToken)
    {
        var delay = TimeSpan.FromSeconds(1);
        using var listenAttempt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        listenAttempt.CancelAfter(delay);
        try
        {
            using var discovery = new LanDiscovery(discoveryPort);
            while (true)
            {
                var room = await discovery.ReceiveAsync(listenAttempt.Token).ConfigureAwait(false);
                if (room.RoomId == initial.Snapshot.RoomId)
                {
                    return room.Endpoint;
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception error) when (error is SocketException or NetworkInformationException)
        {
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            return null;
        }
    }
}
