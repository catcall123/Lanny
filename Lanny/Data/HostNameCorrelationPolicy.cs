using Lanny.Models;

namespace Lanny.Data;

internal static class HostNameCorrelationPolicy
{
    private static readonly HashSet<string> GenericHostNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "android",
        "localhost",
        "unknown",
    };

    public static bool IsSupersededBy(Device existing, Device current)
    {
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(current);

        if (string.Equals(existing.MacAddress, current.MacAddress, StringComparison.OrdinalIgnoreCase))
            return false;

        if (existing.LastSeen >= current.LastSeen)
            return false;

        var existingHostName = NormalizeHostNameForCorrelation(existing.Hostname);
        var currentHostName = NormalizeHostNameForCorrelation(current.Hostname);

        return existingHostName is not null
            && string.Equals(existingHostName, currentHostName, StringComparison.Ordinal);
    }

    private static string? NormalizeHostNameForCorrelation(string? hostName)
    {
        if (string.IsNullOrWhiteSpace(hostName))
            return null;

        var normalized = hostName.Trim().TrimEnd('.');
        if (normalized.EndsWith(".local", StringComparison.OrdinalIgnoreCase))
            normalized = normalized[..^6].TrimEnd('.');

        if (string.IsNullOrWhiteSpace(normalized))
            return null;

        return GenericHostNames.Contains(normalized)
            ? null
            : normalized.ToUpperInvariant();
    }
}
