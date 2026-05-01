using System.Net;
using System.Text;
using Lanny.Models;

namespace Lanny.Discovery;

public static class SsdpMessageParser
{
    private const string DiscoveryMethod = "SSDP";

    public static bool TryParse(byte[] data, IPEndPoint remoteEndPoint, DateTimeOffset capturedAt, out Device? device)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(remoteEndPoint);

        device = null;
        if (data.Length == 0)
            return false;

        var text = Encoding.UTF8.GetString(data);
        var headers = ParseHeaders(text);
        if (headers.Count == 0)
            return false;

        if (headers.TryGetValue("NTS", out var notificationSubType) &&
            notificationSubType.Equals("ssdp:byebye", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var ipAddress = SelectIpAddress(headers, remoteEndPoint);
        if (string.IsNullOrWhiteSpace(ipAddress))
            return false;

        device = new Device
        {
            MacAddress = string.Empty,
            IpAddress = ipAddress,
            Vendor = headers.GetValueOrDefault("SERVER"),
            HttpHeaders = headers,
            DiscoveryMethod = DiscoveryMethod,
            LastSeen = capturedAt,
        };
        return true;
    }

    private static Dictionary<string, string> ParseHeaders(string text)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawLine in text.Split(["\r\n", "\n"], StringSplitOptions.None).Skip(1))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
                break;

            var separator = line.IndexOf(':');
            if (separator <= 0)
                continue;

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            if (key.Length > 0 && value.Length > 0)
                headers[key] = value;
        }

        return headers;
    }

    private static string? SelectIpAddress(IReadOnlyDictionary<string, string> headers, IPEndPoint remoteEndPoint)
    {
        if (remoteEndPoint.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork &&
            !IPAddress.Any.Equals(remoteEndPoint.Address))
        {
            return remoteEndPoint.Address.ToString();
        }

        if (!headers.TryGetValue("LOCATION", out var location) || !Uri.TryCreate(location, UriKind.Absolute, out var uri))
            return null;

        return IPAddress.TryParse(uri.Host, out var ipAddress) && ipAddress.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
            ? ipAddress.ToString()
            : null;
    }
}
