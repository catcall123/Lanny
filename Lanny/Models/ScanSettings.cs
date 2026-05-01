namespace Lanny.Models;

public class ScanSettings
{
    public string Subnet { get; set; } = "auto";
    public int ScanIntervalSeconds { get; set; } = 60;
    public string ArpScanInterface { get; set; } = "auto";
    public bool EnableArpScan { get; set; } = true;
    public bool EnablePingScan { get; set; } = true;
    public bool EnableMdns { get; set; } = true;
    public bool EnableDhcpSnooping { get; set; } = true;
    public bool EnablePassiveArpListener { get; set; } = true;
    public bool EnableSsdpListener { get; set; } = true;
    public string PassiveCaptureInterface { get; set; } = "any";
    public int PassiveObservationRetentionMinutes { get; set; } = 5;
    public bool EnableSnmpInspection { get; set; } = true;
    public string SnmpCommunity { get; set; } = "public";
    public int SnmpTimeoutMs { get; set; } = 1000;
    public bool EnableServiceFingerprinting { get; set; } = true;
    public int FingerprintTimeoutMs { get; set; } = 1500;
    public int FingerprintMaxConcurrency { get; set; } = 8;
    public int OfflineThresholdMinutes { get; set; } = 5;
    public int OfflineDeviceRetentionHours { get; set; } = 24;
    public int StalledScanWarningMinutes { get; set; } = 10;
}
