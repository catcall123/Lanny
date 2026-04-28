namespace Lanny.Models;

public static class MacAddress
{
    /// <summary>Canonical form: uppercase, colon-separated.</summary>
    public static string Normalize(string? mac)
    {
        if (string.IsNullOrWhiteSpace(mac))
            return string.Empty;

        return mac.Trim().Replace('-', ':').ToUpperInvariant();
    }
}
