using System.Net;
using System.Security.Cryptography;
using System.Text;
using Gomoku.Core.Lan;

namespace Gomoku.App;

internal static class LanRejoinTicket
{
    private static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Gomoku", "Rejoin");
    private static readonly TimeSpan Retention = TimeSpan.FromSeconds(LanRoomAuthority.MaxReconnectWaitSeconds + 5);

    public static bool Save(string roomId, TcpLanRoomConnection guest)
    {
        var path = TicketPath(roomId);
        var staging = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            Directory.CreateDirectory(Folder);
            var cutoff = DateTime.UtcNow - Retention;
            foreach (var file in Directory.EnumerateFiles(Folder, "*.seat*"))
            {
                if (File.GetLastWriteTimeUtc(file) < cutoff)
                {
                    Delete(file);
                }
            }

            var token = Encoding.ASCII.GetBytes(guest.RejoinToken);
            var protectedToken = ProtectedData.Protect(token, Entropy(roomId, guest.Endpoint), DataProtectionScope.CurrentUser);
            File.WriteAllBytes(staging, protectedToken);
            File.Move(staging, path, overwrite: true);
            return true;
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or CryptographicException)
        {
            return false;
        }
        finally
        {
            Delete(staging);
        }
    }

    public static string? Read(LanRoomAdvertisement room)
    {
        var path = TicketPath(room.RoomId);
        try
        {
            if (DateTime.UtcNow - File.GetLastWriteTimeUtc(path) > Retention)
            {
                return null;
            }

            var token = ProtectedData.Unprotect(File.ReadAllBytes(path),
                Entropy(room.RoomId, room.Endpoint), DataProtectionScope.CurrentUser);
            return Encoding.ASCII.GetString(token);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or CryptographicException)
        {
            return null;
        }
    }

    public static void Forget(string roomId) => Delete(TicketPath(roomId));

    private static string TicketPath(string roomId)
    {
        var name = Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(roomId)));
        return Path.Combine(Folder, $"{name}.seat");
    }

    private static byte[] Entropy(string roomId, IPEndPoint endpoint) => Encoding.ASCII.GetBytes($"{roomId}@{endpoint}");

    private static void Delete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
        }
    }
}
