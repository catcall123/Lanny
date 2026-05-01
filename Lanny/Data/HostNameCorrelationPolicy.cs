using Lanny.Models;

namespace Lanny.Data;

internal static class HostNameCorrelationPolicy
{
    public static bool IsSupersededBy(Device existing, Device current)
    {
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(current);

        if (string.Equals(existing.MacAddress, current.MacAddress, StringComparison.OrdinalIgnoreCase))
            return false;

        if (existing.LastSeen >= current.LastSeen)
            return false;

        var existingHostName = HostNameQualification.NormalizeForCorrelation(existing.Hostname);
        var currentHostName = HostNameQualification.NormalizeForCorrelation(current.Hostname);

        return existingHostName is not null
            && string.Equals(existingHostName, currentHostName, StringComparison.Ordinal);
    }
}
