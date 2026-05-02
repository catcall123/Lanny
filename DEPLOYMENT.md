# Deployment

Lanny is deployed on the Podman host `192.168.2.43` (`vm-docker`) in the same image-push style used by LRScreen FotoService.

## Build And Push

Build on the Windows development machine with Docker Desktop:

```powershell
docker build -f "C:\Users\Mike\source\AI\Lanny\Lanny\Dockerfile" --force-rm -t lanny --label "com.microsoft.visual-studio.project-name=Lanny" "C:\Users\Mike\source\AI\Lanny\Lanny"
docker tag lanny 192.168.2.43:5000/lanny:latest
docker push 192.168.2.43:5000/lanny:latest
```

The pushed deployment image is:

```text
192.168.2.43:5000/lanny:latest
```

## Podman Host

Connect to the host with SSH:

```powershell
& "C:\Program Files\PuTTY\plink.exe" -batch -no-antispoof -ssh -pw "<password>" mike@192.168.2.43 "hostname; whoami"
```

Run Lanny as a **rootful** Podman container. Rootless Podman can start the app, but LAN discovery is incomplete because DHCP/raw-socket operations are permission-limited.

```bash
sudo podman pull --tls-verify=false 192.168.2.43:5000/lanny:latest
sudo podman stop lanny 2>/dev/null || true
sudo podman rm lanny 2>/dev/null || true
sudo podman run -d \
  --name lanny \
  --restart=unless-stopped \
  --network=host \
  --cap-add=NET_RAW \
  --cap-add=NET_ADMIN \
  -v lanny-data:/app/data \
  -e ASPNETCORE_URLS=http://+:8080 \
  -e "ConnectionStrings__Default=Data Source=/app/data/lanny.db" \
  -e ScanSettings__Subnet=auto \
  -e ScanSettings__ArpScanInterface=auto \
  -e ScanSettings__EnablePassiveArpListener=true \
  -e ScanSettings__EnableSsdpListener=true \
  -e ScanSettings__PassiveCaptureInterface=any \
  -e ScanSettings__PassiveObservationRetentionMinutes=5 \
  192.168.2.43:5000/lanny:latest
```

## Persistence

The SQLite database is persisted in the named rootful Podman volume:

```text
lanny-data:/app/data
```

Inside the container:

```text
/app/data/lanny.db
```

On the Podman host:

```text
/var/lib/containers/storage/volumes/lanny-data/_data/lanny.db
```

Rootless and rootful Podman have separate volume stores. The active deployment is rootful; keep using rootful commands for redeployments so the existing database is reused.

## Verification

Check the container:

```bash
sudo podman ps --filter name=lanny --format '{{.Names}} {{.Image}} {{.Status}}'
sudo podman inspect lanny --format 'RestartPolicy={{.HostConfig.RestartPolicy.Name}} Status={{.State.Status}} StartedAt={{.State.StartedAt}}'
```

Check health and API from the Windows development machine:

```powershell
Invoke-WebRequest -UseBasicParsing -Uri http://192.168.2.43:8080/healthz
Invoke-RestMethod -Uri http://192.168.2.43:8080/api/devices
```

Primary endpoints:

```text
Web UI:  http://192.168.2.43:8080/
Health:  http://192.168.2.43:8080/healthz
API:     http://192.168.2.43:8080/api/devices
```

Useful log checks:

```bash
sudo podman logs --since 5m lanny
sudo podman logs --since 5m lanny | grep -Ei 'ARP scan found|DHCP listener|warn:|error:'
```

Expected healthy discovery signals include:

```text
DHCP listener bound to UDP/67
ARP scan found <n> devices
```

## Notes

- The restart policy is `unless-stopped`, so the container restarts after process exit and host reboot unless it was manually stopped.
- Host networking is required for LAN broadcast visibility.
- `NET_RAW` and `NET_ADMIN` are required for ARP scanning and packet capture.
- The current MQTT broker is on `192.168.2.42:1883`; credentials are hard-coded in `MqttNetMessagePublisher`.
