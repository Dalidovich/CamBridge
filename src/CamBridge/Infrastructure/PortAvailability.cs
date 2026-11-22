using System.Net;
using System.Net.Sockets;
using CamBridge.Configuration;

namespace CamBridge.Infrastructure;

public static class PortAvailability
{
    public static string? DescribeConflict(AppSettings settings)
    {
        if (!IsFree(IPAddress.Any, settings.PhonePort))
        {
            return $"Error: phone port {settings.PhonePort} is already in use. Close the other program or change PhonePort in {PortablePaths.SettingsFile}.";
        }

        if (!IsFree(IPAddress.Loopback, settings.ApiPort))
        {
            return $"Error: API port {settings.ApiPort} is already in use. Close the other program or change ApiPort in {PortablePaths.SettingsFile}.";
        }

        return null;
    }

    public static bool IsFree(IPAddress address, int port)
    {
        try
        {
            using var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            socket.ExclusiveAddressUse = true;
            socket.Bind(new IPEndPoint(address, port));
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }
}
