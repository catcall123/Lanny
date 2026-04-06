namespace Lanny.Models;

public class ScanSettings
{
    public string Subnet { get; set; } = "192.168.1.0/24";
    public int ScanIntervalSeconds { get; set; } = 60;
    public bool EnableArpScan { get; set; } = true;
    public bool EnablePingScan { get; set; } = true;
    public bool EnableMdns { get; set; } = true;
    public bool EnableSnmpInspection { get; set; } = true;
    public string SnmpCommunity { get; set; } = "public";
    public int SnmpTimeoutMs { get; set; } = 1000;
    public bool EnableServiceFingerprinting { get; set; }
    public int FingerprintTimeoutMs { get; set; } = 1000;
    public int FingerprintMaxConcurrency { get; set; } = 4;
    public int OfflineThresholdMinutes { get; set; } = 5;
    public int OfflineDeviceRetentionHours { get; set; } = 168;
    public int StalledScanWarningMinutes { get; set; } = 10;
}
