using System.Collections.Frozen;
using System.Net;
using Lanny.Models;

namespace Lanny.Discovery;

public static class MdnsDeviceDecoder
{
    private static readonly FrozenDictionary<string, string> ServiceVendorHints = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["_googlecast._tcp"] = "Google",
        ["_airplay._tcp"] = "Apple",
        ["_raop._tcp"] = "Apple",
        ["_companion-link._tcp"] = "Apple",
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenSet<string> FriendlyNameKeys = new[]
    {
        "fn",
        "friendlyname",
        "name",
        "devicename",
        "device-name",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenSet<string> ExplicitVendorKeys = new[]
    {
        "manufacturer",
        "vendor",
        "brand",
        "maker",
        "mfg",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenSet<string> VendorHintKeys = new[]
    {
        "ty",
        "model",
        "md",
        "product",
        "deviceid",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenDictionary<string, string> VendorNameHints = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Amazon"] = "Amazon",
        ["Apple"] = "Apple",
        ["Brother"] = "Brother",
        ["Bose"] = "Bose",
        ["Canon"] = "Canon",
        ["Dell"] = "Dell",
        ["Epson"] = "Epson",
        ["Google"] = "Google",
        ["HP"] = "HP",
        ["LG"] = "LG",
        ["Lenovo"] = "Lenovo",
        ["Netgear"] = "NETGEAR",
        ["Nintendo"] = "Nintendo",
        ["Roku"] = "Roku",
        ["Samsung"] = "Samsung",
        ["Sonos"] = "Sonos",
        ["Sony"] = "Sony",
        ["Synology"] = "Synology",
        ["TP-Link"] = "TP-Link",
        ["Ubiquiti"] = "Ubiquiti",
        ["Xiaomi"] = "Xiaomi",
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    public static Device? Decode(
        string? serviceInstanceName,
        string? serviceType,
        string? hostName,
        IEnumerable<string> textRecords,
        IEnumerable<IPAddress> addresses,
        DateTimeOffset observedAt)
    {
        ArgumentNullException.ThrowIfNull(textRecords);
        ArgumentNullException.ThrowIfNull(addresses);

        var properties = ParseProperties(textRecords);
        var hostname = SelectHostName(serviceInstanceName, hostName, properties);
        var vendor = SelectVendor(serviceType, serviceInstanceName, properties);
        var ipAddress = addresses.FirstOrDefault()?.ToString();

        if (string.IsNullOrWhiteSpace(hostname) &&
            string.IsNullOrWhiteSpace(vendor) &&
            string.IsNullOrWhiteSpace(ipAddress))
        {
            return null;
        }

        return new Device
        {
            MacAddress = string.Empty,
            IpAddress = ipAddress,
            Hostname = hostname,
            Vendor = vendor,
            DiscoveryMethod = "mDNS",
            LastSeen = observedAt,
        };
    }

    private static Dictionary<string, string> ParseProperties(IEnumerable<string> textRecords)
    {
        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var textRecord in textRecords)
        {
            if (string.IsNullOrWhiteSpace(textRecord))
                continue;

            var separatorIndex = textRecord.IndexOf('=');
            if (separatorIndex <= 0 || separatorIndex == textRecord.Length - 1)
                continue;

            var key = textRecord[..separatorIndex].Trim();
            var value = textRecord[(separatorIndex + 1)..].Trim();
            if (key.Length == 0 || value.Length == 0)
                continue;

            properties[key] = value;
        }

        return properties;
    }

    private static string? SelectHostName(
        string? serviceInstanceName,
        string? hostName,
        IReadOnlyDictionary<string, string> properties)
    {
        foreach (var property in properties)
        {
            if (FriendlyNameKeys.Contains(property.Key))
                return NormalizeHostName(property.Value);
        }

        var instanceName = ExtractInstanceName(serviceInstanceName);
        var modelHint = SelectModelHint(properties);
        if (IsOpaqueGeneratedHostName(instanceName) && !string.IsNullOrWhiteSpace(modelHint))
            return modelHint;

        if (!string.IsNullOrWhiteSpace(instanceName))
            return NormalizeHostName(instanceName);

        if (IsOpaqueGeneratedHostName(hostName) && !string.IsNullOrWhiteSpace(modelHint))
            return modelHint;

        return NormalizeHostName(hostName);
    }

    private static string? SelectModelHint(IReadOnlyDictionary<string, string> properties)
    {
        foreach (var key in new[] { "ty", "product", "model", "md" })
        {
            if (properties.TryGetValue(key, out var value))
            {
                var normalized = NormalizeHostName(value);
                if (!string.IsNullOrWhiteSpace(normalized))
                    return normalized;
            }
        }

        return null;
    }

    private static string? SelectVendor(
        string? serviceType,
        string? serviceInstanceName,
        IReadOnlyDictionary<string, string> properties)
    {
        foreach (var property in properties)
        {
            if (ExplicitVendorKeys.Contains(property.Key))
                return property.Value;
        }

        foreach (var property in properties)
        {
            if (!VendorHintKeys.Contains(property.Key))
                continue;

            var inferredVendor = TryInferVendor(property.Value);
            if (!string.IsNullOrWhiteSpace(inferredVendor))
                return inferredVendor;
        }

        var instanceVendor = TryInferVendor(serviceInstanceName);
        if (!string.IsNullOrWhiteSpace(instanceVendor))
            return instanceVendor;

        if (!string.IsNullOrWhiteSpace(serviceType) &&
            ServiceVendorHints.TryGetValue(serviceType, out var vendor))
        {
            return vendor;
        }

        return null;
    }

    private static string? TryInferVendor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        foreach (var vendor in VendorNameHints)
        {
            if (value.Contains(vendor.Key, StringComparison.OrdinalIgnoreCase))
                return vendor.Value;
        }

        return null;
    }

    private static string? ExtractInstanceName(string? serviceInstanceName)
    {
        if (string.IsNullOrWhiteSpace(serviceInstanceName))
            return null;

        var delimiterIndex = serviceInstanceName.IndexOf("._", StringComparison.Ordinal);
        return delimiterIndex <= 0
            ? serviceInstanceName
            : serviceInstanceName[..delimiterIndex];
    }

    private static string? NormalizeHostName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var normalized = DecodeDnsSdEscapes(value).Trim().TrimEnd('.');
        if (normalized.EndsWith(".local", StringComparison.OrdinalIgnoreCase))
            normalized = normalized[..^6];

        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static bool IsOpaqueGeneratedHostName(string? value)
    {
        var normalized = NormalizeHostName(value);
        if (string.IsNullOrWhiteSpace(normalized))
            return false;

        return normalized.All(Uri.IsHexDigit) && normalized.Length >= 10;
    }

    private static string DecodeDnsSdEscapes(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var builder = new System.Text.StringBuilder(value.Length);

        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] != '\\')
            {
                builder.Append(value[index]);
                continue;
            }

            if (index + 3 < value.Length &&
                char.IsAsciiDigit(value[index + 1]) &&
                char.IsAsciiDigit(value[index + 2]) &&
                char.IsAsciiDigit(value[index + 3]))
            {
                var code = (value[index + 1] - '0') * 100 +
                           (value[index + 2] - '0') * 10 +
                           (value[index + 3] - '0');
                builder.Append((char)code);
                index += 3;
                continue;
            }

            if (index + 1 < value.Length)
            {
                builder.Append(value[index + 1]);
                index += 1;
                continue;
            }

            builder.Append('\\');
        }

        return builder.ToString();
    }
}
