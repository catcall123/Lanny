using System.Net;
using System.Net.Http.Json;
using Lanny.Api;
using Lanny.Data;
using Lanny.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Lanny.Tests.Api;

public class DeviceEndpointsTests
{
    [Fact]
    public async Task GetDevices_ReturnsAllTrackedDevices()
    {
        await using var host = await EndpointTestHost.CreateAsync();

        await host.Repository.UpsertAsync(new Device
        {
            MacAddress = "AA:BB:CC:DD:EE:01",
            IpAddress = "192.168.1.10",
            DiscoveryMethod = "ARP",
            LastSeen = new DateTimeOffset(2026, 4, 6, 12, 0, 0, TimeSpan.Zero),
        });
        await host.Repository.UpsertAsync(new Device
        {
            MacAddress = "AA:BB:CC:DD:EE:02",
            IpAddress = "192.168.1.11",
            DiscoveryMethod = "ARP",
            LastSeen = new DateTimeOffset(2026, 4, 6, 12, 1, 0, TimeSpan.Zero),
        });

        var devices = await host.Client.GetFromJsonAsync<List<Device>>("/api/devices/");

        Assert.NotNull(devices);
        Assert.Equal(2, devices.Count);
        Assert.Contains(devices, device => device.MacAddress == "AA:BB:CC:DD:EE:01");
        Assert.Contains(devices, device => device.MacAddress == "AA:BB:CC:DD:EE:02");
    }

    [Fact]
    public async Task GetDevice_WithKnownMac_ReturnsMatchingDeviceCaseInsensitively()
    {
        await using var host = await EndpointTestHost.CreateAsync();

        await host.Repository.UpsertAsync(new Device
        {
            MacAddress = "AA:BB:CC:DD:EE:FF",
            IpAddress = "192.168.1.25",
            Hostname = "media-center",
            SystemName = "switch-core",
            SystemDescription = "Cisco IOS XE",
            SystemObjectId = "1.3.6.1.4.1.9.1.1208",
            SystemUptime = 123456,
            InterfaceCount = 24,
            HttpTitle = "Router Admin",
            HttpHeaders = new Dictionary<string, string>
            {
                ["server"] = "nginx",
            },
            TlsCertificateSubject = "CN=router.local",
            TlsSubjectAlternativeNames = ["router.local", "router"],
            SshBanner = "SSH-2.0-OpenSSH_9.6",
            DiscoveryMethod = "ARP",
            LastSeen = new DateTimeOffset(2026, 4, 6, 12, 0, 0, TimeSpan.Zero),
        });

        var json = await host.Client.GetStringAsync("/api/devices/aa:bb:cc:dd:ee:ff");
        var device = await host.Client.GetFromJsonAsync<Device>("/api/devices/aa:bb:cc:dd:ee:ff");

        Assert.NotNull(device);
        Assert.Equal("AA:BB:CC:DD:EE:FF", device.MacAddress);
        Assert.Equal("media-center", device.Hostname);
        Assert.Equal("switch-core", device.SystemName);
        Assert.Equal("Cisco IOS XE", device.SystemDescription);
        Assert.Equal("1.3.6.1.4.1.9.1.1208", device.SystemObjectId);
        Assert.Equal(123456, device.SystemUptime);
        Assert.Equal(24, device.InterfaceCount);
        Assert.Equal("Router Admin", device.HttpTitle);
        Assert.Equal("nginx", device.HttpHeaders!["server"]);
        Assert.Equal("CN=router.local", device.TlsCertificateSubject);
        Assert.Equal(["router.local", "router"], device.TlsSubjectAlternativeNames);
        Assert.Equal("SSH-2.0-OpenSSH_9.6", device.SshBanner);
        Assert.Contains("\"systemName\":\"switch-core\"", json);
        Assert.Contains("\"systemDescription\":\"Cisco IOS XE\"", json);
        Assert.Contains("\"systemObjectId\":\"1.3.6.1.4.1.9.1.1208\"", json);
        Assert.Contains("\"systemUptime\":123456", json);
        Assert.Contains("\"interfaceCount\":24", json);
        Assert.Contains("\"httpTitle\":\"Router Admin\"", json);
        Assert.Contains("\"httpHeaders\":{\"server\":\"nginx\"}", json);
        Assert.Contains("\"tlsCertificateSubject\":\"CN=router.local\"", json);
        Assert.Contains("\"tlsSubjectAlternativeNames\":[\"router.local\",\"router\"]", json);
        Assert.Contains("\"sshBanner\":\"SSH-2.0-OpenSSH_9.6\"", json);
    }

    [Fact]
    public async Task GetDevice_WhenSnmpMetadataIsUnavailable_OmitsSnmpFieldsFromJson()
    {
        await using var host = await EndpointTestHost.CreateAsync();

        await host.Repository.UpsertAsync(new Device
        {
            MacAddress = "AA:BB:CC:DD:EE:11",
            IpAddress = "192.168.1.20",
            Hostname = "printer",
            DiscoveryMethod = "ARP",
            LastSeen = new DateTimeOffset(2026, 4, 6, 12, 0, 0, TimeSpan.Zero),
        });

        var json = await host.Client.GetStringAsync("/api/devices/aa:bb:cc:dd:ee:11");

        Assert.DoesNotContain("systemName", json, StringComparison.Ordinal);
        Assert.DoesNotContain("systemDescription", json, StringComparison.Ordinal);
        Assert.DoesNotContain("systemObjectId", json, StringComparison.Ordinal);
        Assert.DoesNotContain("systemUptime", json, StringComparison.Ordinal);
        Assert.DoesNotContain("interfaceCount", json, StringComparison.Ordinal);
        Assert.DoesNotContain("httpTitle", json, StringComparison.Ordinal);
        Assert.DoesNotContain("httpHeaders", json, StringComparison.Ordinal);
        Assert.DoesNotContain("tlsCertificateSubject", json, StringComparison.Ordinal);
        Assert.DoesNotContain("tlsSubjectAlternativeNames", json, StringComparison.Ordinal);
        Assert.DoesNotContain("sshBanner", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetDevice_WithUnknownMac_ReturnsNotFound()
    {
        await using var host = await EndpointTestHost.CreateAsync();

        var response = await host.Client.GetAsync("/api/devices/00:00:00:00:00:00");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private sealed class EndpointTestHost : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private EndpointTestHost(SqliteConnection connection, WebApplication app)
        {
            _connection = connection;
            App = app;
            Client = app.GetTestClient();
        }

        public WebApplication App { get; }

        public HttpClient Client { get; }

        public DeviceRepository Repository => App.Services.GetRequiredService<DeviceRepository>();

        public static async Task<EndpointTestHost> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = Environments.Development,
            });
            builder.WebHost.UseTestServer();
            builder.Services.AddLogging();
            builder.Services.AddDbContext<LannyDbContext>(options => options.UseSqlite(connection));
            builder.Services.AddSingleton<DeviceRepository>();

            var app = builder.Build();

            await using (var scope = app.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<LannyDbContext>();
                await LannyDbSchemaUpdater.EnsureCreatedAndUpdatedAsync(db);
            }

            app.MapDeviceApi();
            await app.StartAsync();

            return new EndpointTestHost(connection, app);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await App.StopAsync();
            await App.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}