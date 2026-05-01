using Lanny;
using Lanny.Api;
using Lanny.Data;
using Lanny.Discovery;
using Lanny.Hubs;
using Lanny.Models;
using Lanny.Runtime;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// Configuration
builder.Services.Configure<ScanSettings>(builder.Configuration.GetSection("ScanSettings"));
builder.Services.Configure<OuiDatasetOptions>(builder.Configuration.GetSection("OuiDataset"));
builder.Services.AddOptions<SnmpSettings>()
    .Configure<IOptions<ScanSettings>>((snmpSettings, scanSettings) =>
    {
        snmpSettings.Enabled = scanSettings.Value.EnableSnmpInspection;
        snmpSettings.Community = scanSettings.Value.SnmpCommunity;
        snmpSettings.TimeoutMilliseconds = scanSettings.Value.SnmpTimeoutMs;
        snmpSettings.Port = 161;
    });
builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(10));

// Database
builder.Services.AddDbContext<LannyDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Default") ?? "Data Source=lanny.db"));

// Core services
builder.Services.AddSingleton<DeviceRepository>();
builder.Services.AddSingleton<ScanLoopMonitor>();
builder.Services.AddSingleton<IReverseDnsLookup, ReverseDnsLookup>();
builder.Services.AddSingleton<INetBiosNameService, NetBiosNameService>();
builder.Services.AddSingleton<IHostNameResolver, HostNameResolver>();
builder.Services.AddSingleton<IScanSubnetResolver, GatewaySubnetResolver>();
builder.Services.AddHttpClient<IOuiDatasetHttpClient, OuiDatasetHttpClient>();
builder.Services.AddSingleton<OuiDatasetRefresher>();
builder.Services.AddSingleton<ISnmpClient, SharpSnmpClient>();
builder.Services.AddSingleton<ISnmpMetadataProvider, SnmpMetadataProvider>();
builder.Services.AddSingleton<IDiscoveryService, SelfDiscoveryService>();
builder.Services.AddSingleton<IDiscoveryService, PingScanner>();
builder.Services.AddSingleton<IDiscoveryService, ArpScanner>();
builder.Services.AddSingleton<IDiscoveryService, MdnsListener>();
builder.Services.AddSingleton<IDiscoveryService, SnmpScanner>();
builder.Services.AddSingleton<DhcpListener>();
builder.Services.AddSingleton<IDiscoveryService>(sp => sp.GetRequiredService<DhcpListener>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<DhcpListener>());
builder.Services.AddSingleton<PassiveArpListener>();
builder.Services.AddSingleton<IDiscoveryService>(sp => sp.GetRequiredService<PassiveArpListener>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<PassiveArpListener>());
builder.Services.AddSingleton<SsdpListener>();
builder.Services.AddSingleton<IDiscoveryService>(sp => sp.GetRequiredService<SsdpListener>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<SsdpListener>());
builder.Services.AddSingleton<IServiceFingerprintProbe, HttpFingerprintProbe>();
builder.Services.AddSingleton<IServiceFingerprintProbe, TlsCertificateProbe>();
builder.Services.AddSingleton<IServiceFingerprintProbe, SshBannerProbe>();
builder.Services.AddSingleton<IDiscoveryService, ServiceFingerprintScanner>();
builder.Services.AddHealthChecks().AddCheck<ScanLoopHealthCheck>("scan_loop");

// Worker
builder.Services.AddHostedService<Worker>();
builder.Services.AddHostedService<WorkerWatchdog>();
builder.Services.AddHostedService<OuiDatasetRefreshService>();

// SignalR + static files
builder.Services.AddSignalR();

var app = builder.Build();

// Ensure database is created
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<LannyDbContext>();
    await LannyDbSchemaUpdater.EnsureCreatedAndUpdatedAsync(db);
}

// Middleware
app.UseDefaultFiles();
app.UseStaticFiles();

// Endpoints
app.MapDeviceApi();
app.MapHealthChecks("/healthz");
app.MapHub<DeviceHub>("/hubs/devices");

app.Run();
