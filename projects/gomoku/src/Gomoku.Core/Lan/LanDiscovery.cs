using System.Buffers.Binary;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace Gomoku.Core.Lan;

public sealed record LanRoomAdvertisement(string RoomId, IPEndPoint Endpoint, bool IsAi);

public sealed class LanDiscovery : IDisposable
{
    public const int DefaultPort = 47831;
    private static ReadOnlySpan<byte> Header => "GOMOKU/2"u8;
    private static int PortOffset => Header.Length + LanRoomAuthority.RoomIdLength;
    private static int DatagramSize => PortOffset + sizeof(ushort) + sizeof(byte);
    private static readonly IPAddress LoopbackBroadcast = IPAddress.Parse("127.255.255.255");
    private readonly UdpClient listener = new(AddressFamily.InterNetwork);

    public int Port { get; }

    public LanDiscovery(int port = DefaultPort)
    {
        try
        {
            listener.ExclusiveAddressUse = false;
            listener.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            listener.Client.Bind(new IPEndPoint(IPAddress.Any, port));
            Port = ((IPEndPoint)listener.Client.LocalEndPoint!).Port;
        }
        catch
        {
            listener.Dispose();
            throw;
        }
    }

    public async ValueTask<LanRoomAdvertisement> ReceiveAsync(CancellationToken cancellationToken = default)
    {
        while (true)
        {
            var packet = await listener.ReceiveAsync(cancellationToken).ConfigureAwait(false);
            if (Parse(packet) is { } room)
            {
                return room;
            }
        }
    }

    public void Dispose() => listener.Dispose();

    public static async Task AdvertiseAsync(string roomId, int roomPort, CancellationToken cancellationToken,
        int discoveryPort = DefaultPort, bool isAi = false)
    {
        if (!LanRoomAuthority.IsRoomId(roomId))
        {
            throw new ArgumentException("Invalid room ID.", nameof(roomId));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(roomPort, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(roomPort, ushort.MaxValue);
        ArgumentOutOfRangeException.ThrowIfLessThan(discoveryPort, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(discoveryPort, ushort.MaxValue);
        var datagram = new byte[DatagramSize];
        Header.CopyTo(datagram);
        Encoding.ASCII.GetBytes(roomId, datagram.AsSpan(Header.Length));
        BinaryPrimitives.WriteUInt16BigEndian(datagram.AsSpan(PortOffset), (ushort)roomPort);
        datagram[^1] = isAi ? (byte)1 : (byte)0;

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        do
        {
            try
            {
                await BroadcastAsync(datagram, discoveryPort, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception error) when (error is IOException or SocketException or NetworkInformationException)
            {
            }
        } while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false));
    }

    private static LanRoomAdvertisement? Parse(UdpReceiveResult packet)
    {
        var buffer = packet.Buffer.AsSpan();
        if (buffer.Length != DatagramSize || !buffer.StartsWith(Header) || buffer[^1] > 1)
        {
            return null;
        }

        var roomId = Encoding.ASCII.GetString(buffer.Slice(Header.Length, LanRoomAuthority.RoomIdLength));
        var port = BinaryPrimitives.ReadUInt16BigEndian(buffer[PortOffset..]);
        return port != 0 && LanRoomAuthority.IsRoomId(roomId)
            ? new(roomId, new(packet.RemoteEndPoint.Address, port), buffer[^1] == 1)
            : null;
    }

    private static async Task BroadcastAsync(byte[] datagram, int port, CancellationToken cancellationToken)
    {
        await SendAsync(datagram, IPAddress.Loopback, LoopbackBroadcast, port, cancellationToken).ConfigureAwait(false);
        SocketException? failure = null;
        var sentOnLan = false;
        foreach (var (address, broadcast) in InterfaceBroadcasts())
        {
            try
            {
                await SendAsync(datagram, address, broadcast, port, cancellationToken).ConfigureAwait(false);
                sentOnLan = true;
            }
            catch (SocketException error)
            {
                failure = error;
            }
        }

        if (!sentOnLan && failure is not null)
        {
            throw new IOException("LAN discovery broadcast failed.", failure);
        }
    }

    private static async ValueTask SendAsync(byte[] datagram, IPAddress address, IPAddress broadcast, int port,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var sender = new UdpClient(AddressFamily.InterNetwork) { EnableBroadcast = true };
        sender.Client.Bind(new IPEndPoint(address, 0));
        await sender.SendAsync(datagram, new IPEndPoint(broadcast, port), cancellationToken).ConfigureAwait(false);
    }

    private static IEnumerable<(IPAddress Address, IPAddress Broadcast)> InterfaceBroadcasts()
    {
        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (adapter.OperationalStatus != OperationalStatus.Up || adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback)
            {
                continue;
            }

            foreach (var info in adapter.GetIPProperties().UnicastAddresses)
            {
                if (info.Address.AddressFamily != AddressFamily.InterNetwork || info.PrefixLength is < 1 or > 30)
                {
                    continue;
                }

                var address = info.Address.GetAddressBytes();
                var mask = info.IPv4Mask.GetAddressBytes();
                for (var index = 0; index < address.Length; index++)
                {
                    address[index] = (byte)(address[index] | ~mask[index]);
                }

                yield return (info.Address, new IPAddress(address));
            }
        }
    }
}
