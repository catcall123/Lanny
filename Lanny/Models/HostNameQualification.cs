namespace Lanny.Models;

public static class HostNameQualification
{
    private static readonly HashSet<string> GenericHostNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "android",
        "localhost",
        "unknown",
    };

    public static bool IsQualified(string? hostName) => NormalizeForCorrelation(hostName) is not null;

    public static string? NormalizeForCorrelation(string? hostName)
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
