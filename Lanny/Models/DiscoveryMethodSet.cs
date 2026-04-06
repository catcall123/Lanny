namespace Lanny.Models;

public static class DiscoveryMethodSet
{
    public static string? Normalize(string? methods) => Merge(null, methods);

    public static string? Merge(string? current, string? additional)
    {
        var mergedMethods = new List<string>();
        AddMethods(mergedMethods, current);
        AddMethods(mergedMethods, additional);
        return mergedMethods.Count == 0 ? null : string.Join(',', mergedMethods);
    }

    private static void AddMethods(ICollection<string> mergedMethods, string? methods)
    {
        if (string.IsNullOrWhiteSpace(methods))
            return;

        foreach (var method in methods.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (mergedMethods.Contains(method, StringComparer.OrdinalIgnoreCase))
                continue;

            mergedMethods.Add(method);
        }
    }
}