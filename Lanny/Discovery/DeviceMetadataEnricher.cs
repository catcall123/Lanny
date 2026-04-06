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
        target.SystemName ??= observation.SystemName;
        target.SystemDescription ??= observation.SystemDescription;
        target.SystemObjectId ??= observation.SystemObjectId;
        target.SystemUptime ??= observation.SystemUptime;
        target.InterfaceCount ??= observation.InterfaceCount;
        target.HttpTitle ??= observation.HttpTitle;
        target.TlsCertificateSubject ??= observation.TlsCertificateSubject;
        target.SshBanner ??= observation.SshBanner;
        MergeHeaders(target, observation);
        MergeSubjectAlternativeNames(target, observation);

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

    private static void MergeHeaders(Device target, Device observation)
    {
        if (observation.HttpHeaders is null || observation.HttpHeaders.Count == 0)
            return;

        target.HttpHeaders ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in observation.HttpHeaders)
        {
            target.HttpHeaders.TryAdd(key, value);
        }
    }

    private static void MergeSubjectAlternativeNames(Device target, Device observation)
    {
        if (observation.TlsSubjectAlternativeNames is null || observation.TlsSubjectAlternativeNames.Count == 0)
            return;

        target.TlsSubjectAlternativeNames ??= [];
        foreach (var subjectAlternativeName in observation.TlsSubjectAlternativeNames)
        {
            if (!target.TlsSubjectAlternativeNames.Contains(subjectAlternativeName, StringComparer.OrdinalIgnoreCase))
                target.TlsSubjectAlternativeNames.Add(subjectAlternativeName);
        }
    }
}
