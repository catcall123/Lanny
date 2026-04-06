using Lanny;
using Lanny.Api;
using Lanny.Data;
using Lanny.Discovery;
using Lanny.Hubs;
using Lanny.Models;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Configuration
builder.Services.Configure<ScanSettings>(builder.Configuration.GetSection("ScanSettings"));

// Database
builder.Services.AddDbContext<LannyDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Default") ?? "Data Source=lanny.db"));

// Core services
builder.Services.AddSingleton<DeviceRepository>();
builder.Services.AddSingleton<IDiscoveryService, ArpScanner>();
builder.Services.AddSingleton<IDiscoveryService, PingScanner>();
builder.Services.AddSingleton<IDiscoveryService, MdnsListener>();

// Worker
builder.Services.AddHostedService<Worker>();

// SignalR + static files
builder.Services.AddSignalR();

var app = builder.Build();

// Ensure database is created
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<LannyDbContext>();
    await db.Database.EnsureCreatedAsync();
}

// Middleware
app.UseDefaultFiles();
app.UseStaticFiles();

// Endpoints
app.MapDeviceApi();
app.MapHub<DeviceHub>("/hubs/devices");

app.Run();
