using System.Net;
using Lanny.Models;
using Lanny.Runtime;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Lanny.Tests.Api;

public class HealthEndpointTests
{
    [Fact]
    public async Task Healthz_WhenScanLoopCompletedRecently_ReturnsOk()
    {
        var monitor = new ScanLoopMonitor();
        var cycleNumber = monitor.BeginCycle(DateTimeOffset.UtcNow.AddSeconds(-1));
        monitor.CompleteCycle(cycleNumber, DateTimeOffset.UtcNow);

        await using var app = await CreateAppAsync(monitor, options => options.StalledScanWarningMinutes = 10);

        var response = await app.GetTestClient().GetAsync("/healthz");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Healthz_WhenScanLoopIsStale_ReturnsServiceUnavailable()
    {
        var monitor = new ScanLoopMonitor();
        var staleCompletedAt = DateTimeOffset.UtcNow.AddMinutes(-20);
        var cycleNumber = monitor.BeginCycle(staleCompletedAt.AddMinutes(-1));
        monitor.CompleteCycle(cycleNumber, staleCompletedAt);

        await using var app = await CreateAppAsync(monitor, options => options.StalledScanWarningMinutes = 5);

        var response = await app.GetTestClient().GetAsync("/healthz");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    private static async Task<WebApplication> CreateAppAsync(ScanLoopMonitor monitor, Action<ScanSettings> configureSettings)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development,
        });
        builder.WebHost.UseTestServer();
        builder.Services.Configure(configureSettings);
        builder.Services.AddSingleton(monitor);
        builder.Services.AddHealthChecks().AddCheck<ScanLoopHealthCheck>("scan_loop");

        var app = builder.Build();
        app.MapHealthChecks("/healthz");
        await app.StartAsync();
        return app;
    }
}