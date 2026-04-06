namespace Lanny.Discovery;

public static class SshBannerParser
{
    public static string? Parse(string? banner)
    {
        if (string.IsNullOrWhiteSpace(banner))
            return null;

        var normalized = banner.Trim();
        return string.IsNullOrWhiteSpace(normalized)
            ? null
            : normalized;
    }
}