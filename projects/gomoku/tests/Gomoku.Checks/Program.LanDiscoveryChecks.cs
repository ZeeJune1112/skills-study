using System.Net;
using Gomoku.Core.Lan;

namespace Gomoku.Checks;

internal static partial class Program
{
    [Check]
    private static void LanUdpBroadcastReachesBothSharedPortListeners() =>
        CheckLanDiscoveryAsync(isAi: false).GetAwaiter().GetResult();

    [Check]
    private static void LanUdpBroadcastIdentifiesAiRooms() =>
        CheckLanDiscoveryAsync(isAi: true).GetAwaiter().GetResult();

    private static async Task CheckLanDiscoveryAsync(bool isAi)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var first = new LanDiscovery(0);
        using var second = new LanDiscovery(first.Port);
        using var room = new LanRoomAuthority();
        const int roomPort = 54321;
        using var advertising = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
        var publish = LanDiscovery.AdvertiseAsync(room.RoomId, roomPort, advertising.Token, first.Port, isAi);
        try
        {
            await Task.WhenAll(ExpectBroadcastAsync(first), ExpectBroadcastAsync(second));
        }
        finally
        {
            await advertising.CancelAsync();
            try
            {
                await publish;
            }
            catch (OperationCanceledException) when (advertising.IsCancellationRequested)
            {
            }
        }

        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();
        try
        {
            await first.ReceiveAsync(canceled.Token);
            throw new InvalidOperationException("The discovery listener ignored cancellation.");
        }
        catch (OperationCanceledException)
        {
        }

        async Task ExpectBroadcastAsync(LanDiscovery listener)
        {
            while (true)
            {
                var roomFound = await listener.ReceiveAsync(deadline.Token);
                if (roomFound.RoomId == room.RoomId && IPAddress.IsLoopback(roomFound.Endpoint.Address))
                {
                    Require(roomFound.Endpoint.Port == roomPort, "Discovery should return the advertised TCP port.");
                    Require(roomFound.IsAi == isAi, "Discovery should return the advertised room type.");
                    return;
                }
            }
        }
    }
}
