using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace CamBridge.Infrastructure;

public static class LanAddressResolver
{
    private static readonly string[] VirtualAdapterMarkers =
    [
        "Virtual", "Hyper-V", "VMware", "VirtualBox", "WSL", "vEthernet", "Bluetooth", "TAP"
    ];

    public static NetworkSelection Resolve(string? preferredIp)
    {
        var candidates = CollectCandidates();
        var all = candidates.Select(candidate => candidate.Address).Distinct().ToList();
        var primary = FindPreferred(preferredIp, all) ?? PickBest(candidates) ?? IPAddress.Loopback;
        var others = all.Where(address => !address.Equals(primary)).ToList();
        return new NetworkSelection(primary, others, all);
    }

    private static IPAddress? FindPreferred(string? preferredIp, List<IPAddress> all)
    {
        if (string.IsNullOrWhiteSpace(preferredIp) || !IPAddress.TryParse(preferredIp.Trim(), out var parsed))
        {
            return null;
        }

        return all.FirstOrDefault(address => address.Equals(parsed));
    }

    private static IPAddress? PickBest(List<AddressCandidate> candidates)
    {
        return candidates
            .Where(candidate => !candidate.IsVirtual && candidate.HasGateway)
            .OrderByDescending(candidate => IsPrivate(candidate.Address))
            .Select(candidate => candidate.Address)
            .FirstOrDefault()
            ?? candidates
                .Where(candidate => !candidate.IsVirtual)
                .OrderByDescending(candidate => IsPrivate(candidate.Address))
                .Select(candidate => candidate.Address)
                .FirstOrDefault();
    }

    private static List<AddressCandidate> CollectCandidates()
    {
        var result = new List<AddressCandidate>();
        NetworkInterface[] interfaces;
        try
        {
            interfaces = NetworkInterface.GetAllNetworkInterfaces();
        }
        catch (NetworkInformationException)
        {
            return result;
        }

        foreach (var networkInterface in interfaces)
        {
            if (networkInterface.OperationalStatus != OperationalStatus.Up
                || networkInterface.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
            {
                continue;
            }

            IPInterfaceProperties properties;
            try
            {
                properties = networkInterface.GetIPProperties();
            }
            catch (NetworkInformationException)
            {
                continue;
            }

            var isVirtual = IsVirtualAdapter(networkInterface);
            var hasGateway = properties.GatewayAddresses.Any(gateway =>
                gateway.Address.AddressFamily == AddressFamily.InterNetwork && !gateway.Address.Equals(IPAddress.Any));

            foreach (var unicast in properties.UnicastAddresses)
            {
                var address = unicast.Address;
                if (address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(address) || IsLinkLocal(address))
                {
                    continue;
                }

                result.Add(new AddressCandidate(address, isVirtual, hasGateway));
            }
        }

        return result;
    }

    private static bool IsVirtualAdapter(NetworkInterface networkInterface)
    {
        return VirtualAdapterMarkers.Any(marker =>
            networkInterface.Name.Contains(marker, StringComparison.OrdinalIgnoreCase)
            || networkInterface.Description.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsLinkLocal(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return bytes[0] == 169 && bytes[1] == 254;
    }

    private static bool IsPrivate(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return bytes[0] == 10
            || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
            || (bytes[0] == 192 && bytes[1] == 168);
    }

    private sealed record AddressCandidate(IPAddress Address, bool IsVirtual, bool HasGateway);
}
