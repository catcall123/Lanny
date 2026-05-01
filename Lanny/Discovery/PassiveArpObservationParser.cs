using System.Text.RegularExpressions;
using Lanny.Models;
using MacAddress = Lanny.Models.MacAddress;

namespace Lanny.Discovery;

public static partial class PassiveArpObservationParser
{
    private const string DiscoveryMethod = "PassiveARP";

    public static bool TryParseTcpdumpLine(string? line, DateTimeOffset capturedAt, out Device? device)
    {
        device = null;
        if (string.IsNullOrWhiteSpace(line))
            return false;

        var reply = ArpReplyRegex().Match(line);
        if (reply.Success)
            return TryCreateDevice(reply.Groups["ip"].Value, reply.Groups["mac"].Value, capturedAt, out device);

        var request = ArpRequestRegex().Match(line);
        if (request.Success)
            return TryCreateDevice(request.Groups["ip"].Value, request.Groups["mac"].Value, capturedAt, out device);

        return false;
    }

    private static bool TryCreateDevice(string ipAddress, string macAddress, DateTimeOffset capturedAt, out Device? device)
    {
        device = null;

        var normalizedMac = MacAddress.Normalize(macAddress);
        if (!ArpEntryFilter.IsRelevantNeighbor(ipAddress, normalizedMac))
            return false;

        device = new Device
        {
            MacAddress = normalizedMac,
            IpAddress = ipAddress,
            Vendor = OuiLookup.Resolve(normalizedMac),
            DiscoveryMethod = DiscoveryMethod,
            LastSeen = capturedAt,
        };
        return true;
    }

    [GeneratedRegex(@"Reply\s+(?<ip>\d+\.\d+\.\d+\.\d+)\s+is-at\s+(?<mac>[\da-fA-F]{2}(?::[\da-fA-F]{2}){5})", RegexOptions.IgnoreCase)]
    private static partial Regex ArpReplyRegex();

    [GeneratedRegex(@"(?<mac>[\da-fA-F]{2}(?::[\da-fA-F]{2}){5})\s+>\s+.*Request\s+who-has\s+\d+\.\d+\.\d+\.\d+\s+tell\s+(?<ip>\d+\.\d+\.\d+\.\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex ArpRequestRegex();
}
