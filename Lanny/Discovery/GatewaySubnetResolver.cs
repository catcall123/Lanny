using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Lanny.Discovery;

public sealed class GatewaySubnetResolver : IScanSubnetResolver
{
    private const string AutoSubnetValue = "auto";

    public string ResolveSubnet(string? configuredSubnet)
    {
        if (!string.IsNullOrWhiteSpace(configuredSubnet) &&
            !configuredSubnet.Equals(AutoSubnetValue, StringComparison.OrdinalIgnoreCase))
        {
            return configuredSubnet.Trim();
        }

        var subnet = TryResolveSubnet(GetGatewayCandidates());
        return subnet ?? throw new InvalidOperationException("Could not derive the scan subnet from an active default gateway.");
    }

    public static string? TryResolveSubnet(IEnumerable<GatewayInterfaceCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        var candidate = candidates
            .Where(IsUsableCandidate)
            .OrderByDescending(entry => IsPrivateAddress(entry.LocalAddress))
            .ThenBy(entry => entry.InterfaceType == NetworkInterfaceType.Ethernet ? 0 : 1)
            .FirstOrDefault();

        if (candidate is null)
            return null;

        var networkAddress = CalculateNetworkAddress(candidate.LocalAddress, candidate.PrefixLength);
        return $"{networkAddress}/{candidate.PrefixLength}";
    }

    private static IEnumerable<GatewayInterfaceCandidate> GetGatewayCandidates()
    {
        foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
        {
            var properties = networkInterface.GetIPProperties();
            var gateways = properties.GatewayAddresses
                .Select(entry => entry.Address)
                .Where(address => address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.Any.Equals(address) && !IPAddress.None.Equals(address))
                .ToArray();

            if (gateways.Length == 0)
                continue;

            foreach (var unicastAddress in properties.UnicastAddresses.Where(entry => entry.Address.AddressFamily == AddressFamily.InterNetwork))
            {
                var prefixLength = GetPrefixLength(unicastAddress);
                if (prefixLength is <= 0 or > 30)
                    continue;

                foreach (var gateway in gateways)
                {
                    yield return new GatewayInterfaceCandidate(
                        unicastAddress.Address,
                        gateway,
                        prefixLength,
                        networkInterface.NetworkInterfaceType,
                        networkInterface.OperationalStatus);
                }
            }
        }
    }

    private static bool IsUsableCandidate(GatewayInterfaceCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        if (candidate.OperationalStatus != OperationalStatus.Up)
            return false;

        if (candidate.InterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
            return false;

        if (candidate.PrefixLength is <= 0 or > 30)
            return false;

        var localNetwork = CalculateNetworkAddress(candidate.LocalAddress, candidate.PrefixLength);
        var gatewayNetwork = CalculateNetworkAddress(candidate.GatewayAddress, candidate.PrefixLength);
        return localNetwork.Equals(gatewayNetwork);
    }

    private static int GetPrefixLength(UnicastIPAddressInformation unicastAddress)
    {
        ArgumentNullException.ThrowIfNull(unicastAddress);

        if (unicastAddress.PrefixLength > 0)
            return unicastAddress.PrefixLength;

        if (unicastAddress.IPv4Mask is null)
            return -1;

        var mask = unicastAddress.IPv4Mask.GetAddressBytes();
        var prefixLength = 0;
        foreach (var octet in mask)
        {
            prefixLength += byte.PopCount(octet);
        }

        return prefixLength;
    }

    private static bool IsPrivateAddress(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return bytes[0] == 10
            || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
            || (bytes[0] == 192 && bytes[1] == 168);
    }

    private static IPAddress CalculateNetworkAddress(IPAddress address, int prefixLength)
    {
        var addressValue = ToUInt32(address);
        var mask = prefixLength == 0 ? 0u : uint.MaxValue << (32 - prefixLength);
        return FromUInt32(addressValue & mask);
    }

    private static uint ToUInt32(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
    }

    private static IPAddress FromUInt32(uint value)
    {
        return new IPAddress([
            (byte)(value >> 24),
            (byte)(value >> 16),
            (byte)(value >> 8),
            (byte)value,
        ]);
    }
}

public sealed record GatewayInterfaceCandidate(
    IPAddress LocalAddress,
    IPAddress GatewayAddress,
    int PrefixLength,
    NetworkInterfaceType InterfaceType,
    OperationalStatus OperationalStatus);