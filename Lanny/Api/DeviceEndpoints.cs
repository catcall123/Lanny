using Lanny.Data;
using Lanny.Models;
using Lanny.Runtime;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Lanny.Api;

public static class DeviceEndpoints
{
    public static void MapDeviceApi(this WebApplication app)
    {
        var group = app.MapGroup("/api/devices");

        group.MapGet("/", (DeviceRepository repo, IOptions<ScanSettings> settings) =>
        {
            var cutoff = DateTimeOffset.UtcNow.AddMinutes(-settings.Value.OfflineThresholdMinutes);
            var devices = repo.GetAll();
            foreach (var d in devices)
                d.IsOnline = d.LastSeen >= cutoff;
            return Results.Ok(devices);
        });

        group.MapGet("/{mac}", (string mac, DeviceRepository repo, IOptions<ScanSettings> settings) =>
        {
            var device = repo.Get(mac);
            if (device is null)
                return Results.NotFound();

            var cutoff = DateTimeOffset.UtcNow.AddMinutes(-settings.Value.OfflineThresholdMinutes);
            device.IsOnline = device.LastSeen >= cutoff;
            return Results.Ok(device);
        });

        // Scan-loop staleness signal so the UI can warn when data is frozen.
        // Healthy = a cycle completed within 2x the configured scan interval.
        app.MapGet("/api/scan-loop", ([FromServices] ScanLoopMonitor monitor, IOptions<ScanSettings> settings) =>
        {
            var snapshot = monitor.GetSnapshot();
            var stalenessThreshold = TimeSpan.FromSeconds(settings.Value.ScanIntervalSeconds * 2);
            var lastCompleted = snapshot.LastCycleCompletedAtUtc;
            var isHealthy = lastCompleted.HasValue
                && DateTimeOffset.UtcNow - lastCompleted.Value < stalenessThreshold;

            return Results.Ok(new
            {
                isHealthy,
                lastCompletedAtUtc = lastCompleted,
                lastCompletedCycleNumber = snapshot.LastCompletedCycleNumber,
                currentCycleNumber = snapshot.CurrentCycleNumber,
                workerStartedAtUtc = snapshot.StartedAtUtc,
            });
        });
    }
}
