namespace Lanny.Models;

public class Device
{
    /// <summary>MAC address — primary identifier.</summary>
    public string MacAddress { get; set; } = string.Empty;

    public string? IpAddress { get; set; }
    public string? Hostname { get; set; }
    public string? Vendor { get; set; }
    public string? DiscoveryMethod { get; set; }
    public List<int> OpenPorts { get; set; } = [];
    public DateTimeOffset FirstSeen { get; set; }
    public DateTimeOffset LastSeen { get; set; }
    public bool IsOnline { get; set; }
}
