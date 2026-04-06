using Lanny.Discovery;
using Lanny.Models;

namespace Lanny.Tests.Discovery;

public class DeviceMetadataEnricherTests
{
    [Fact]
    public void MergeRelatedObservations_FillsMissingHostnameVendorAndDiscoveryMethod()
    {
        var device = new Device
        {
            MacAddress = "AA:BB:CC:DD:EE:FF",
            IpAddress = "192.168.1.10",
            DiscoveryMethod = "ARP",
            LastSeen = DateTimeOffset.UtcNow,
        };

        DeviceMetadataEnricher.MergeRelatedObservations(device,
        [
            new Device
            {
                MacAddress = string.Empty,
                IpAddress = "192.168.1.10",
                Hostname = "workstation.local",
                DiscoveryMethod = "Ping",
            },
            new Device
            {
                MacAddress = string.Empty,
                IpAddress = "192.168.1.10",
                Vendor = "Google",
                DiscoveryMethod = "mDNS",
            },
            new Device
            {
                MacAddress = string.Empty,
                IpAddress = "192.168.1.99",
                Hostname = "ignored-host",
                Vendor = "Ignored Vendor",
                DiscoveryMethod = "mDNS",
            },
        ]);

        Assert.Equal("workstation.local", device.Hostname);
        Assert.Equal("Google", device.Vendor);
        Assert.Equal("ARP,Ping,mDNS", device.DiscoveryMethod);
    }

    [Fact]
    public void MergeObservation_WhenSnmpMetadataExists_MergesStructuredSystemFields()
    {
        var device = new Device
        {
            MacAddress = "AA:BB:CC:DD:EE:FF",
            IpAddress = "192.168.1.10",
            DiscoveryMethod = "ARP",
            LastSeen = DateTimeOffset.UtcNow,
        };

        DeviceMetadataEnricher.MergeObservation(device, new Device
        {
            IpAddress = "192.168.1.10",
            Hostname = "core-switch",
            SystemName = "core-switch",
            SystemDescription = "Cisco IOS XE",
            SystemObjectId = "1.3.6.1.4.1.9.1.1208",
            SystemUptime = 123456,
            InterfaceCount = 24,
            DiscoveryMethod = "SNMP",
        });

        Assert.Equal("core-switch", device.Hostname);
        Assert.Equal("core-switch", device.SystemName);
        Assert.Equal("Cisco IOS XE", device.SystemDescription);
        Assert.Equal("1.3.6.1.4.1.9.1.1208", device.SystemObjectId);
        Assert.Equal(123456, device.SystemUptime);
        Assert.Equal(24, device.InterfaceCount);
        Assert.Equal("ARP,SNMP", device.DiscoveryMethod);
    }

    [Fact]
    public void MergeObservation_WhenServiceFingerprintMetadataExists_PreservesHigherConfidenceHostnameAndAddsProtocolMetadata()
    {
        var device = new Device
        {
            MacAddress = "AA:BB:CC:DD:EE:10",
            IpAddress = "192.168.1.15",
            Hostname = "printer.local",
            DiscoveryMethod = "mDNS",
        };

        DeviceMetadataEnricher.MergeObservation(device, new Device
        {
            IpAddress = "192.168.1.15",
            Hostname = "wrong-http-hostname",
            HttpTitle = "Printer Console",
            HttpHeaders = new Dictionary<string, string>
            {
                ["server"] = "nginx",
            },
            TlsCertificateSubject = "CN=printer.local",
            TlsSubjectAlternativeNames = ["printer.local"],
            SshBanner = "SSH-2.0-OpenSSH_9.6",
            DiscoveryMethod = "HTTP,TLS,SSH",
        });

        Assert.Equal("printer.local", device.Hostname);
        Assert.Equal("Printer Console", device.HttpTitle);
        Assert.Equal("nginx", device.HttpHeaders!["server"]);
        Assert.Equal("CN=printer.local", device.TlsCertificateSubject);
        Assert.Equal(["printer.local"], device.TlsSubjectAlternativeNames);
        Assert.Equal("SSH-2.0-OpenSSH_9.6", device.SshBanner);
        Assert.Equal("mDNS,HTTP,TLS,SSH", device.DiscoveryMethod);
    }

    [Fact]
    public void MergeObservation_WhenCompositeDiscoveryMethodsOverlap_DeduplicatesIndividualTags()
    {
        var device = new Device
        {
            MacAddress = "AA:BB:CC:DD:EE:11",
            IpAddress = "192.168.1.16",
            DiscoveryMethod = "ARP",
        };

        DeviceMetadataEnricher.MergeObservation(device, new Device
        {
            IpAddress = "192.168.1.16",
            DiscoveryMethod = "ARP,mDNS",
        });

        Assert.Equal("ARP,mDNS", device.DiscoveryMethod);
    }
}
