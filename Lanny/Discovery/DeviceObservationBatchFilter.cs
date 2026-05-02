using Lanny.Models;

namespace Lanny.Discovery;

public static class DeviceObservationBatchFilter
{
    public static IReadOnlyList<Device> RemoveSupersededHostnamePairings(IReadOnlyList<Device> devices)
    {
        ArgumentNullException.ThrowIfNull(devices);

        return devices
            .Where(device => !HasNewerDifferentMacWithSameHostName(device, devices))
            .ToList();
    }

    private static bool HasNewerDifferentMacWithSameHostName(Device candidate, IReadOnlyList<Device> devices)
    {
        var candidateHostName = HostNameQualification.NormalizeForCorrelation(candidate.Hostname);
        if (candidateHostName is null)
            return false;

        return devices.Any(device =>
            !ReferenceEquals(device, candidate)
            && !string.Equals(device.MacAddress, candidate.MacAddress, StringComparison.OrdinalIgnoreCase)
            && device.LastSeen > candidate.LastSeen
            && string.Equals(
                HostNameQualification.NormalizeForCorrelation(device.Hostname),
                candidateHostName,
                StringComparison.Ordinal));
    }
}
