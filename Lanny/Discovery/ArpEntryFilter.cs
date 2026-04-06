using System.Globalization;
using System.Net;
using System.Net.Sockets;
using Lanny.Models;

namespace Lanny.Discovery;

public static class ArpEntryFilter
{
    public static bool IsRelevantNeighbor(string ipAddress, string macAddress)
    {
        if (string.IsNullOrWhiteSpace(ipAddress) || string.IsNullOrWhiteSpace(macAddress))
            return false;

        if (!IPAddress.TryParse(ipAddress, out var parsedAddress) || parsedAddress.AddressFamily != AddressFamily.InterNetwork)
            return false;

        if (IsMulticastOrBroadcastIpv4(parsedAddress))
            return false;

        return IsUnicastMacAddress(macAddress);
    }

    public static bool IsNoiseOnlyDevice(Device device)
    {
        ArgumentNullException.ThrowIfNull(device);

        return string.Equals(DiscoveryMethodSet.Normalize(device.DiscoveryMethod), "ARP", StringComparison.OrdinalIgnoreCase)
            && !IsRelevantNeighbor(device.IpAddress ?? string.Empty, device.MacAddress);
    }

    private static bool IsMulticastOrBroadcastIpv4(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return address.Equals(IPAddress.Broadcast)
            || bytes[0] is >= 224 and <= 239;
    }

    private static bool IsUnicastMacAddress(string macAddress)
    {
        var segments = macAddress.Split([':', '-'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length != 6)
            return false;

        if (segments.All(segment => segment.Equals("00", StringComparison.OrdinalIgnoreCase)) ||
            segments.All(segment => segment.Equals("FF", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        return byte.TryParse(segments[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var firstOctet)
            && (firstOctet & 1) == 0;
    }
}