using Lanny.Models;

namespace Lanny.Discovery;

public static class DeviceMetadataEnricher
{
    public static void MergeRelatedObservations(Device device, IEnumerable<Device> observations)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(observations);

        if (string.IsNullOrWhiteSpace(device.IpAddress))
            return;

        foreach (var observation in observations)
        {
            if (!string.Equals(device.IpAddress, observation.IpAddress, StringComparison.OrdinalIgnoreCase))
                continue;

            MergeObservation(device, observation);
        }
    }

    public static void MergeObservation(Device target, Device observation)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(observation);

        target.Hostname ??= observation.Hostname;
        target.IpAddress ??= observation.IpAddress;
        target.Vendor = SelectPreferredVendor(target.Vendor, observation.Vendor);

        if (!string.IsNullOrWhiteSpace(observation.DiscoveryMethod) &&
            target.DiscoveryMethod?.Contains(observation.DiscoveryMethod, StringComparison.OrdinalIgnoreCase) != true)
        {
            target.DiscoveryMethod = string.IsNullOrWhiteSpace(target.DiscoveryMethod)
                ? observation.DiscoveryMethod
                : $"{target.DiscoveryMethod},{observation.DiscoveryMethod}";
        }
    }

    private static string? SelectPreferredVendor(string? currentVendor, string? candidateVendor)
    {
        if (string.IsNullOrWhiteSpace(candidateVendor))
            return currentVendor;

        if (string.IsNullOrWhiteSpace(currentVendor))
            return candidateVendor;

        return currentVendor.Equals("Private", StringComparison.OrdinalIgnoreCase)
            ? candidateVendor
            : currentVendor;
    }
}
