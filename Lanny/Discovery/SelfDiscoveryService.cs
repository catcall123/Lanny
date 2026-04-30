using System.Net.NetworkInformation;
using System.Net.Sockets;
using Lanny.Models;

namespace Lanny.Discovery;

/// <summary>
/// Emits a Device record for each of the host machine's own network interfaces.
/// The host running Lanny is otherwise invisible to ARP (its own MAC is not in
/// its own neighbor table) and only intermittently visible to the DHCP listener
/// (only on lease renewal). Without a steady MAC-bearing observation, Ping
/// hits get dropped and the host drifts offline in the device list.
/// </summary>
public sealed class SelfDiscoveryService : IDiscoveryService
{
    public string Name => "Self";

    public Task<IReadOnlyList<Device>> ScanAsync(CancellationToken ct)
    {
        var devices = new List<Device>();
        var hostname = Environment.MachineName;
        var now = DateTimeOffset.UtcNow;

        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up)
                continue;
            if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                continue;

            var macBytes = nic.GetPhysicalAddress().GetAddressBytes();
            if (macBytes.Length != 6)
                continue;

            var mac = MacAddress.Normalize(string.Join(":", macBytes.Select(b => b.ToString("X2"))));

            foreach (var unicast in nic.GetIPProperties().UnicastAddresses)
            {
                if (unicast.Address.AddressFamily != AddressFamily.InterNetwork)
                    continue;

                devices.Add(new Device
                {
                    MacAddress = mac,
                    IpAddress = unicast.Address.ToString(),
                    Hostname = hostname,
                    DiscoveryMethod = Name,
                    LastSeen = now,
                });
            }
        }

        return Task.FromResult<IReadOnlyList<Device>>(devices);
    }
}
