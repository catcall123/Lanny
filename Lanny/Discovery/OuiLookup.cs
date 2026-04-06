using System.Collections.Frozen;

namespace Lanny.Discovery;

/// <summary>Resolves the first 3 bytes of a MAC address to a vendor name using a bundled OUI dataset.</summary>
public static class OuiLookup
{
    private const string LocallyAdministeredLabel = "Locally Administered / Randomized";
    private static readonly Lock SyncLock = new();
    private static FrozenDictionary<string, string> _prefixes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        .ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    private static string? _loadedDatasetPath;
    private static DateTime _loadedLastWriteUtc;

    public static string? Resolve(string mac)
    {
        if (string.IsNullOrWhiteSpace(mac))
            return null;

        var hex = new string(mac
            .Where(char.IsAsciiHexDigit)
            .Select(char.ToUpperInvariant)
            .ToArray());
        if (hex.Length < 6)
            return null;

        var prefix = OuiVendorDatasetParser.NormalizePrefix(hex);
        if (prefix is null)
            return null;

        var vendor = GetPrefixes().GetValueOrDefault(prefix);
        if (IsLocallyAdministered(hex))
            return string.IsNullOrWhiteSpace(vendor) ? LocallyAdministeredLabel : $"{LocallyAdministeredLabel} ({vendor})";

        return vendor;
    }

    private static FrozenDictionary<string, string> GetPrefixes()
    {
        var datasetPath = ResolveDatasetPath();
        var lastWriteUtc = File.Exists(datasetPath)
            ? File.GetLastWriteTimeUtc(datasetPath)
            : DateTime.MinValue;

        lock (SyncLock)
        {
            if (string.Equals(datasetPath, _loadedDatasetPath, StringComparison.OrdinalIgnoreCase) &&
                lastWriteUtc == _loadedLastWriteUtc)
            {
                return _prefixes;
            }

            _prefixes = LoadPrefixes(datasetPath);
            _loadedDatasetPath = datasetPath;
            _loadedLastWriteUtc = lastWriteUtc;
            return _prefixes;
        }
    }

    private static FrozenDictionary<string, string> LoadPrefixes(string datasetPath)
    {
        if (!File.Exists(datasetPath))
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase).ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

        return OuiVendorDatasetParser.ParseLines(File.ReadLines(datasetPath));
    }

    private static string ResolveDatasetPath()
    {
        var overridePath = Environment.GetEnvironmentVariable("LANNY_OUI_DATASET_PATH");
        return string.IsNullOrWhiteSpace(overridePath)
            ? Path.Combine(AppContext.BaseDirectory, "Data", "oui-prefixes.csv")
            : overridePath;
    }

    private static bool IsLocallyAdministered(string normalizedHex)
    {
        var firstByte = Convert.ToByte(normalizedHex[..2], 16);
        return (firstByte & 0b0000_0010) != 0;
    }
}
