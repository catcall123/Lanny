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

        target.Hostname = SelectPreferredHostName(
            target.Hostname,
            observation.Hostname,
            target.DiscoveryMethod,
            observation.DiscoveryMethod);
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
        target.DiscoveryMethod = DiscoveryMethodSet.Merge(target.DiscoveryMethod, observation.DiscoveryMethod);

        if (observation.LastSeen > target.LastSeen)
            target.LastSeen = observation.LastSeen;
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

    private static string? SelectPreferredHostName(
        string? currentHostName,
        string? candidateHostName,
        string? currentDiscoveryMethod,
        string? candidateDiscoveryMethod)
    {
        if (string.IsNullOrWhiteSpace(candidateHostName))
            return currentHostName;

        if (string.IsNullOrWhiteSpace(currentHostName))
            return candidateHostName;

        if (string.Equals(currentHostName, candidateHostName, StringComparison.OrdinalIgnoreCase))
            return currentHostName;

        var currentLooksGenerated = IsOpaqueGeneratedHostName(currentHostName);
        var candidateLooksGenerated = IsOpaqueGeneratedHostName(candidateHostName);
        if (currentLooksGenerated && !candidateLooksGenerated)
            return candidateHostName;

        if (!currentLooksGenerated && candidateLooksGenerated)
            return currentHostName;

        return GetDiscoveryConfidence(candidateDiscoveryMethod) > GetDiscoveryConfidence(currentDiscoveryMethod)
            ? candidateHostName
            : currentHostName;
    }

    private static int GetDiscoveryConfidence(string? discoveryMethod)
    {
        if (string.IsNullOrWhiteSpace(discoveryMethod))
            return 0;

        if (discoveryMethod.Contains("mDNS", StringComparison.OrdinalIgnoreCase))
            return 4;

        if (discoveryMethod.Contains("SNMP", StringComparison.OrdinalIgnoreCase))
            return 3;

        if (discoveryMethod.Contains("Ping", StringComparison.OrdinalIgnoreCase))
            return 2;

        return 1;
    }

    private static bool IsOpaqueGeneratedHostName(string? hostName)
    {
        if (string.IsNullOrWhiteSpace(hostName))
            return false;

        var normalized = hostName.Trim().TrimEnd('.');
        if (normalized.EndsWith(".local", StringComparison.OrdinalIgnoreCase))
            normalized = normalized[..^6];

        return normalized.Length >= 10 && normalized.All(Uri.IsHexDigit);
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
