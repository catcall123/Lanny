# Lanny

A lightweight network device discovery agent that continuously scans your LAN and presents results in a real-time web dashboard.

## What it does

Lanny runs ARP, ICMP ping, mDNS, passive ARP, SSDP, DHCP, SNMP, and service-fingerprint discovery on a configurable interval to detect devices on your local network. Discovered devices are correlated by MAC address or known IP address, enriched with vendor info via OUI lookup, and persisted to a SQLite database. The web UI updates in real time over SignalR.

### Dashboard features

- Live device table with search, sort, and online/offline filtering
- Stats overview: total, online, offline, unique vendors
- Device detail modal with full history
- Auto-reconnecting SignalR connection

## Quick start

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/)
- Linux host (or container) with `NET_RAW` / `NET_ADMIN` capabilities for ARP scanning and passive packet capture

### Run locally

```bash
dotnet run --project Lanny/Lanny.csproj
```

Open `http://localhost:5000` in your browser.

### Run in a container

```bash
podman compose up -d
```

Or manually:

```bash
podman build -t lanny ./Lanny
podman run -d --name lanny \
  --network=host \
  --cap-add=NET_RAW --cap-add=NET_ADMIN \
  -v lanny-data:/app/data \
  lanny
```

The container requires `--network=host` (or macvlan) for LAN broadcast visibility and `NET_RAW`/`NET_ADMIN` capabilities for raw sockets.

### Ubuntu VM continuous monitoring

Run the container with host networking on the Ubuntu VM that sits on the LAN you want to monitor:

```bash
sudo apt-get update
sudo apt-get install -y podman podman-compose
podman compose up -d --build
podman ps
curl -fsS http://127.0.0.1:8080/healthz
```

If `tcpdump -D` shows a better LAN interface than `any`, set `ScanSettings__PassiveCaptureInterface=<interface>` in `docker-compose.yml`. Keep the VM attached to the same broadcast domain as the devices; passive ARP and SSDP depend on broadcast/multicast visibility.

## Configuration

Edit `appsettings.json` or set environment variables:

| Setting | Default | Description |
|---|---|---|
| `ScanSettings:Subnet` | `192.168.1.0/24` | CIDR subnet to scan |
| `ScanSettings:ScanIntervalSeconds` | `60` | Seconds between scan cycles |
| `ScanSettings:EnableArpScan` | `true` | Enable ARP-based discovery |
| `ScanSettings:EnablePingScan` | `true` | Enable ICMP ping sweep |
| `ScanSettings:EnableMdns` | `true` | Enable mDNS/Bonjour listener |
| `ScanSettings:EnableDhcpSnooping` | `true` | Listen for DHCP client broadcasts |
| `ScanSettings:EnablePassiveArpListener` | `true` | Listen for ARP traffic via `tcpdump` |
| `ScanSettings:EnableSsdpListener` | `true` | Listen for SSDP/UPnP multicast announcements |
| `ScanSettings:PassiveCaptureInterface` | `any` | Interface passed to `tcpdump` for passive ARP |
| `ScanSettings:PassiveObservationRetentionMinutes` | `5` | Minutes to retain passive ARP/SSDP observations |
| `ScanSettings:OfflineThresholdMinutes` | `5` | Minutes before a device is marked offline |

## Build & test

```bash
dotnet build Lanny/Lanny.csproj
dotnet test
```

## Architecture

- **Discovery services** (`ArpScanner`, `PingScanner`, `MdnsListener`, `DhcpListener`, `PassiveArpListener`, `SsdpListener`) — pluggable scanners behind `IDiscoveryService`
- **Worker** — background service that orchestrates scan cycles, correlates results, and pushes updates
- **DeviceRepository** — in-memory device store backed by SQLite via EF Core
- **DeviceHub** — SignalR hub for real-time dashboard updates
- **REST API** — minimal API endpoints under `/api/devices`

## License

Unlicensed — private project.
