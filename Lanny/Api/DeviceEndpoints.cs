using Lanny.Data;

namespace Lanny.Api;

public static class DeviceEndpoints
{
    public static void MapDeviceApi(this WebApplication app)
    {
        var group = app.MapGroup("/api/devices");

        group.MapGet("/", (DeviceRepository repo) => Results.Ok(repo.GetAll()));

        group.MapGet("/{mac}", (string mac, DeviceRepository repo) =>
        {
            var device = repo.Get(mac);
            return device is not null ? Results.Ok(device) : Results.NotFound();
        });
    }
}
