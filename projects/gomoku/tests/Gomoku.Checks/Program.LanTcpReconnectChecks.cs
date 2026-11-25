using System.Net;
using System.Net.Sockets;
using Gomoku.Core;
using Gomoku.Core.Lan;

namespace Gomoku.Checks;

internal static partial class Program
{
    [Check]
    private static void LanTcpDistinguishesVoluntaryExitFromAFailedSocket() =>
        CheckLanTcpVoluntaryExitAsync().GetAwaiter().GetResult();

    private static async Task CheckLanTcpVoluntaryExitAsync()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        foreach (var dropFirst in new[] { false, true })
        {
            await using var room = new TcpLanRoomHost(reconnectWaitSeconds: 30);
            var endpoint = new IPEndPoint(IPAddress.Loopback, room.Port);
            using var guest = await TcpLanRoomConnection.ConnectAsync(endpoint, room.RoomId, deadline.Token);
            await using var guestEvents = guest.ReadEventsAsync(deadline.Token).GetAsyncEnumerator();
            using var observer = await TcpLanRoomConnection.ConnectAsync(endpoint, room.RoomId, deadline.Token);
            await using var observerEvents = observer.ReadEventsAsync(deadline.Token).GetAsyncEnumerator();
            var host = room.Connection;
            await guest.SendAsync(new LanRoomCommand.SelectSeat(Stone.White));
            await ExpectRoomAsync(guestEvents, state => state.Seat == Stone.White);
            await host.SendAsync(new LanRoomCommand.StartMatch());
            await host.SendAsync(new LanRoomCommand.PlaceStone(7, 7));
            await ExpectRoomAsync(observerEvents, state => state.Board.Moves.Count == 1);

            if (dropFirst)
            {
                guest.Dispose();
                await ExpectRoomAsync(observerEvents, state => state.Phase == LanRoomPhase.Paused);
                await guest.NotifyDepartureAsync(deadline.Token);
            }
            else
            {
                await guest.SendAsync(new LanRoomCommand.LeaveRoom());
                while (await guestEvents.MoveNextAsync() && guestEvents.Current is LanRoomEvent.State)
                {
                }

                Require(guestEvents.Current is LanRoomEvent.Closed { Reason: LanRoomCloseReason.Left },
                    "The guest should receive confirmation before the socket closes.");
            }

            Require(await observerEvents.MoveNextAsync() && observerEvents.Current is LanRoomEvent.State
                { Snapshot: { Phase: LanRoomPhase.Aborted, ReconnectWait: null, WhiteOccupied: false,
                    AbortReason: LanMatchAbortReason.PlayerLeft,
                    Board: { Moves.Count: 1, Turn: null, Outcome: MatchOutcome.InProgress } } },
                "An explicit exit should end the match without a result or another wait.");
            await room.DisposeAsync();
            Require(await observerEvents.MoveNextAsync() &&
                observerEvents.Current is LanRoomEvent.Closed { Reason: LanRoomCloseReason.HostClosed },
                "An observer should get a disband notice when the host shuts down.");
        }
    }

    [Check]
    private static void LanTcpDetectsSilentLossAndDiscoversTheHostAtItsNewEndpoint() =>
        CheckLanTcpRejoinAsync().GetAwaiter().GetResult();

    private static async Task CheckLanTcpRejoinAsync()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        await using var room = new TcpLanRoomHost(spectatorCapacity: 1, reconnectWaitSeconds: 15);
        var endpoint = new IPEndPoint(IPAddress.Loopback, room.Port);
        using var discoveryPort = new LanDiscovery(0);
        using var relay = new TcpListener(IPAddress.Loopback, 0);
        relay.Start();
        var connecting = TcpLanRoomConnection.ConnectAsync((IPEndPoint)relay.LocalEndpoint, room.RoomId, deadline.Token);
        using var guestSide = await relay.AcceptTcpClientAsync(deadline.Token);
        using var hostSide = new TcpClient();
        await hostSide.ConnectAsync(endpoint, deadline.Token);
        using var forwarding = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
        var toHost = guestSide.GetStream().CopyToAsync(hostSide.GetStream(), forwarding.Token);
        var fromHost = hostSide.GetStream().CopyToAsync(guestSide.GetStream(), forwarding.Token);

        using var guest = await connecting;
        await using var guestEvents = guest.ReadEventsAsync(deadline.Token).GetAsyncEnumerator();
        await using var hostEvents = room.Connection.ReadEventsAsync(deadline.Token).GetAsyncEnumerator();
        await guest.SendAsync(new LanRoomCommand.SelectSeat(Stone.White));
        await ExpectRoomAsync(hostEvents, state => state.WhiteOccupied);
        await room.Connection.SendAsync(new LanRoomCommand.StartMatch());
        await room.Connection.SendAsync(new LanRoomCommand.PlaceStone(7, 7));
        var playing = await ExpectRoomAsync(guestEvents, state => state.Board.Moves.Count == 1);
        using var observer = await TcpLanRoomConnection.ConnectAsync(endpoint, room.RoomId, deadline.Token);
        await using var observerEvents = observer.ReadEventsAsync(deadline.Token).GetAsyncEnumerator();
        await ExpectRoomAsync(observerEvents, state => state.SpectatorCount == 1);

        await Task.Delay(TimeSpan.FromSeconds(6), deadline.Token);
        await guest.SendAsync(new LanRoomCommand.PlaceStone(7, 8));
        playing = await ExpectRoomAsync(guestEvents, state => state.Board.Moves.Count == 2);
        await forwarding.CancelAsync();
        try
        {
            await Task.WhenAll(toHost, fromHost);
        }
        catch (OperationCanceledException) when (forwarding.IsCancellationRequested)
        {
        }

        relay.Stop();
        try
        {
            await guestEvents.MoveNextAsync();
            throw new InvalidOperationException("The guest did not detect silent host traffic loss.");
        }
        catch (LanRoomException error)
        {
            Require(error.Error == LanRoomError.ConnectionClosed, "The guest should detect a silent connection loss.");
        }

        await ExpectRoomAsync(observerEvents, state => state is
            { Phase: LanRoomPhase.Paused, WhiteOccupied: true, SpectatorCount: 1 });
        var rejoining = guest.RejoinAsync(deadline.Token, discoveryPort.Port);
        using var broadcasting = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
        var advertisement = LanDiscovery.AdvertiseAsync(room.RoomId, room.Port, broadcasting.Token, discoveryPort.Port);
        try
        {
            using var restored = await rejoining;
            await using var restoredEvents = restored.ReadEventsAsync(deadline.Token).GetAsyncEnumerator();
            var resumed = await ExpectRoomAsync(restoredEvents, _ => true);
            Require(resumed is { Seat: Stone.White, Phase: LanRoomPhase.Playing, Board.Turn: Stone.Black } &&
                resumed.Board.Moves.SequenceEqual(playing.Board.Moves), "Discovery should recover the original guest and match.");
            await room.Connection.SendAsync(new LanRoomCommand.PlaceStone(8, 7));
            await ExpectRoomAsync(restoredEvents, state => state.Board.Moves.Count == 3 && state.Board.Turn == Stone.White);
        }
        finally
        {
            await broadcasting.CancelAsync();
            try
            {
                await advertisement;
            }
            catch (OperationCanceledException) when (broadcasting.IsCancellationRequested)
            {
            }
        }
    }
}
