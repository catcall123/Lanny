using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Lanny.Models;
using Microsoft.Extensions.Options;

namespace Lanny.Discovery;

/// <summary>
/// Passively listens on UDP/67 for DHCP DISCOVER and REQUEST broadcasts.
/// Extracts MAC, requested IP, hostname (option 12) and vendor class (option 60)
/// from each packet. Provides observations to the discovery cycle as a snapshot.
/// </summary>
public sealed class DhcpListener : BackgroundService, IDiscoveryService
{
    private const int DhcpServerPort = 67;
    private const int BootpHeaderSize = 240;
    private static readonly byte[] MagicCookie = [0x63, 0x82, 0x53, 0x63];

    private readonly ConcurrentDictionary<string, DhcpObservation> _observations = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<DhcpListener> _logger;
    private readonly ScanSettings _settings;
    private UdpClient? _udp;

    public string Name => "DHCP";

    public DhcpListener(ILogger<DhcpListener> logger, IOptions<ScanSettings> settings)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _settings = settings?.Value ?? throw new ArgumentNullException(nameof(settings));
    }

    public Task<IReadOnlyList<Device>> ScanAsync(CancellationToken ct)
    {
        var retentionCutoff = DateTimeOffset.UtcNow.AddHours(-_settings.OfflineDeviceRetentionHours);
        foreach (var (mac, observation) in _observations)
        {
            if (observation.LastSeen < retentionCutoff)
                _observations.TryRemove(mac, out _);
        }

        var devices = _observations.Values
            .Select(o => new Device
            {
                MacAddress = o.MacAddress,
                IpAddress = o.IpAddress,
                Hostname = o.Hostname,
                Vendor = o.VendorClass,
                DiscoveryMethod = Name,
                LastSeen = o.LastSeen,
            })
            .ToList();

        return Task.FromResult<IReadOnlyList<Device>>(devices);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.EnableDhcpSnooping)
        {
            _logger.LogInformation("DHCP snooping disabled by configuration");
            return;
        }

        try
        {
            _udp = new UdpClient(AddressFamily.InterNetwork);
            _udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _udp.EnableBroadcast = true;
            _udp.Client.Bind(new IPEndPoint(IPAddress.Any, DhcpServerPort));
        }
        catch (SocketException ex)
        {
            _logger.LogWarning(ex, "DHCP listener could not bind UDP/{Port} — service disabled. Another DHCP server on this host?", DhcpServerPort);
            _udp?.Dispose();
            _udp = null;
            return;
        }

        _logger.LogInformation("DHCP listener bound to UDP/{Port}", DhcpServerPort);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = await _udp.ReceiveAsync(stoppingToken);
                TryParseAndStore(result.Buffer);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "DHCP receive error");
            }
        }
    }

    public override void Dispose()
    {
        _udp?.Dispose();
        _udp = null;
        base.Dispose();
        GC.SuppressFinalize(this);
    }

    private void TryParseAndStore(byte[] data)
    {
        var observation = DhcpPacketParser.Parse(data, DateTimeOffset.UtcNow);
        if (observation is null)
            return;

        _observations[observation.MacAddress] = observation;
        _logger.LogDebug(
            "DHCP {MessageType} from {Mac} hostname={Hostname} ip={Ip} vendor={Vendor}",
            observation.MessageType,
            observation.MacAddress,
            observation.Hostname ?? "(none)",
            observation.IpAddress ?? "(none)",
            observation.VendorClass ?? "(none)");
    }

    internal static class DhcpPacketParser
    {
        public static DhcpObservation? Parse(byte[] data, DateTimeOffset capturedAt)
        {
            if (data.Length < BootpHeaderSize + 1)
                return null;

            // op=1 BOOTREQUEST (clients send these to port 67); op=2 BOOTREPLY (server→client on port 68).
            if (data[0] != 1)
                return null;

            // htype=1 Ethernet
            if (data[1] != 1)
                return null;

            var hlen = data[2];
            if (hlen != 6)
                return null;

            // Magic cookie at offset 236 marks DHCP options (vs. plain BOOTP).
            for (var i = 0; i < MagicCookie.Length; i++)
            {
                if (data[236 + i] != MagicCookie[i])
                    return null;
            }

            var mac = FormatMac(data.AsSpan(28, 6));
            var ciaddr = ReadIp(data.AsSpan(12, 4));

            string? hostname = null;
            string? vendorClass = null;
            string? requestedIp = null;
            byte messageType = 0;

            var offset = BootpHeaderSize;
            while (offset < data.Length)
            {
                var code = data[offset++];
                if (code == 0) continue;          // pad
                if (code == 255) break;            // end
                if (offset >= data.Length) break;
                var length = data[offset++];
                if (offset + length > data.Length) break;

                var value = data.AsSpan(offset, length);
                switch (code)
                {
                    case 12:
                        hostname = DecodeAsciiString(value);
                        break;
                    case 50 when length == 4:
                        requestedIp = ReadIp(value);
                        break;
                    case 53 when length == 1:
                        messageType = value[0];
                        break;
                    case 60:
                        vendorClass = DecodeAsciiString(value);
                        break;
                }

                offset += length;
            }

            // 1=DISCOVER, 3=REQUEST, 8=INFORM are the client-side types worth recording.
            if (messageType is not (1 or 3 or 8))
                return null;

            var ipAddress = !string.IsNullOrEmpty(ciaddr) ? ciaddr : requestedIp;

            return new DhcpObservation
            {
                MacAddress = mac,
                IpAddress = ipAddress,
                Hostname = hostname,
                VendorClass = vendorClass,
                MessageType = MessageTypeName(messageType),
                LastSeen = capturedAt,
            };
        }

        private static string FormatMac(ReadOnlySpan<byte> bytes)
        {
            var builder = new StringBuilder(17);
            for (var i = 0; i < bytes.Length; i++)
            {
                if (i > 0) builder.Append(':');
                builder.Append(bytes[i].ToString("X2"));
            }
            return builder.ToString();
        }

        private static string? ReadIp(ReadOnlySpan<byte> bytes)
        {
            if (BinaryPrimitives.ReadUInt32BigEndian(bytes) == 0)
                return null;
            return new IPAddress(bytes.ToArray()).ToString();
        }

        private static string? DecodeAsciiString(ReadOnlySpan<byte> bytes)
        {
            // Some clients null-terminate; some pad with garbage past the printable run.
            var length = bytes.Length;
            for (var i = 0; i < bytes.Length; i++)
            {
                if (bytes[i] == 0)
                {
                    length = i;
                    break;
                }
            }

            if (length == 0)
                return null;

            var text = Encoding.ASCII.GetString(bytes[..length]).Trim();
            return string.IsNullOrEmpty(text) ? null : text;
        }

        private static string MessageTypeName(byte code) => code switch
        {
            1 => "DISCOVER",
            3 => "REQUEST",
            8 => "INFORM",
            _ => $"TYPE-{code}",
        };
    }

    internal sealed class DhcpObservation
    {
        public required string MacAddress { get; init; }
        public string? IpAddress { get; init; }
        public string? Hostname { get; init; }
        public string? VendorClass { get; init; }
        public required string MessageType { get; init; }
        public required DateTimeOffset LastSeen { get; init; }
    }
}
