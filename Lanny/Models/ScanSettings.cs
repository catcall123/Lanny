namespace Lanny.Models;

public class ScanSettings
{
    public string Subnet { get; set; } = "192.168.1.0/24";
    public int ScanIntervalSeconds { get; set; } = 60;
    public bool EnableArpScan { get; set; } = true;
    public bool EnablePingScan { get; set; } = true;
    public bool EnableMdns { get; set; } = true;
    public int OfflineThresholdMinutes { get; set; } = 5;
}
