using System.Collections.Frozen;

namespace Lanny.Discovery;

public static class OuiVendorDatasetParser
{
    public static FrozenDictionary<string, string> ParseLines(IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var dataset = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawLine in lines)
        {
            var line = rawLine?.Trim();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
                continue;

            var separatorIndex = line.IndexOf(',');
            if (separatorIndex < 0)
                continue;

            var prefix = NormalizePrefix(line[..separatorIndex]);
            var vendor = line[(separatorIndex + 1)..].Trim();
            if (string.IsNullOrWhiteSpace(prefix) || string.IsNullOrWhiteSpace(vendor))
                continue;

            dataset[prefix] = vendor;
        }

        return dataset.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }

    public static FrozenDictionary<string, string> ParseIeeeCsvLines(IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var dataset = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var fields in lines.Skip(1).Select(ParseCsvLine))
        {
            if (fields.Count < 3)
                continue;

            var prefix = NormalizePrefix(fields[1]);
            var vendor = fields[2].Trim();
            if (string.IsNullOrWhiteSpace(prefix) || string.IsNullOrWhiteSpace(vendor))
                continue;

            dataset[prefix] = vendor;
        }

        return dataset.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }

    public static string? NormalizePrefix(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var hex = new string(value
            .Where(char.IsAsciiHexDigit)
            .Select(char.ToUpperInvariant)
            .ToArray());
        if (hex.Length < 6)
            return null;

        return string.Create(8, hex, static (buffer, source) =>
        {
            buffer[0] = source[0];
            buffer[1] = source[1];
            buffer[2] = ':';
            buffer[3] = source[2];
            buffer[4] = source[3];
            buffer[5] = ':';
            buffer[6] = source[4];
            buffer[7] = source[5];
        });
    }

    private static List<string> ParseCsvLine(string line)
    {
        var fields = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;

        foreach (var character in line)
        {
            if (character == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (character == ',' && !inQuotes)
            {
                fields.Add(current.ToString());
                current.Clear();
                continue;
            }

            current.Append(character);
        }

        fields.Add(current.ToString());
        return fields;
    }
}