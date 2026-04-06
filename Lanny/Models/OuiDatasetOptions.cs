namespace Lanny.Models;

public class OuiDatasetOptions
{
    public string DatasetPath { get; set; } = Path.Combine(AppContext.BaseDirectory, "Data", "oui-prefixes.csv");

    public bool RefreshOnStartup { get; set; } = true;

    public int RefreshIntervalHours { get; set; } = 168;

    public List<string> SourceUrls { get; set; } =
    [
        "https://standards-oui.ieee.org/oui/oui.csv",
        "https://standards-oui.ieee.org/cid/cid.csv",
    ];
}