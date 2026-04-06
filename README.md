# Lanny

A lightweight network device discovery agent that continuously scans your LAN and presents results in a real-time web dashboard.

## What it does

Lanny runs ARP, ICMP ping, and mDNS scans on a configurable interval to detect devices on your local network. Discovered devices are correlated by MAC address, enriched with vendor info via OUI lookup, and persisted to a SQLite database. The web UI updates in real time over SignalR.

### Dashboard features

- Live device table with search, sort, and online/offline filtering
- Stats overview: total, online, offline, unique vendors
- Device detail modal with full history
- Auto-reconnecting SignalR connection

## Quick start

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/)
- Linux host (or container) with `NET_RAW` / `NET_ADMIN` capabilities for ARP scanning

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

## Configuration

Edit `appsettings.json` or set environment variables:

| Setting | Default | Description |
|---|---|---|
| `ScanSettings:Subnet` | `192.168.1.0/24` | CIDR subnet to scan |
| `ScanSettings:ScanIntervalSeconds` | `60` | Seconds between scan cycles |
| `ScanSettings:EnableArpScan` | `true` | Enable ARP-based discovery |
| `ScanSettings:EnablePingScan` | `true` | Enable ICMP ping sweep |
| `ScanSettings:EnableMdns` | `true` | Enable mDNS/Bonjour listener |
| `ScanSettings:OfflineThresholdMinutes` | `5` | Minutes before a device is marked offline |

## Build & test

```bash
dotnet build Lanny/Lanny.csproj
dotnet test
```

## Architecture

- **Discovery services** (`ArpScanner`, `PingScanner`, `MdnsListener`) — pluggable scanners behind `IDiscoveryService`
- **Worker** — background service that orchestrates scan cycles, correlates results, and pushes updates
- **DeviceRepository** — in-memory device store backed by SQLite via EF Core
- **DeviceHub** — SignalR hub for real-time dashboard updates
- **REST API** — minimal API endpoints under `/api/devices`

## License

Unlicensed — private project.
