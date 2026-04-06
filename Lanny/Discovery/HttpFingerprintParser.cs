using System.Text.RegularExpressions;

namespace Lanny.Discovery;

public static partial class HttpFingerprintParser
{
    public static HttpFingerprintMetadata Parse(IReadOnlyDictionary<string, string> headers, string? body)
    {
        ArgumentNullException.ThrowIfNull(headers);

        var normalizedHeaders = headers.ToDictionary(
            entry => entry.Key,
            entry => entry.Value,
            StringComparer.OrdinalIgnoreCase);

        var title = ParseTitle(body);
        return new HttpFingerprintMetadata(title, normalizedHeaders);
    }

    private static string? ParseTitle(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return null;

        var match = HtmlTitleRegex().Match(body);
        if (!match.Success)
            return null;

        var title = Regex.Replace(match.Groups["title"].Value, "\\s+", " ").Trim();
        return string.IsNullOrWhiteSpace(title)
            ? null
            : title;
    }

    [GeneratedRegex("<title[^>]*>(?<title>.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex HtmlTitleRegex();
}