using Lanny.Data;
using Lanny.Discovery;
using Lanny.Hubs;
using Lanny.Models;
using Lanny.Tests.Support;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Lanny.Tests;

public class WorkerTests
{
    [Fact]
    public async Task ExecuteAsync_CorrelatesDevicesMarksStaleDevicesOfflineAndBroadcastsSnapshot()
    {
        var hubContext = new RecordingHubContext();
        await using var host = await SqliteTestHost.CreateAsync(services =>
        {
            services.AddSingleton<IHubContext<DeviceHub>>(hubContext);
        });

        var repository = host.Services.GetRequiredService<DeviceRepository>();
        await repository.UpsertAsync(new Device
        {
            MacAddress = "11:22:33:44:55:66",
            IpAddress = "192.168.1.5",
            DiscoveryMethod = "ARP",
            LastSeen = DateTimeOffset.UtcNow.AddMinutes(-10),
        });

        var observedAt = DateTimeOffset.UtcNow;
        var scanners = new IDiscoveryService[]
        {
            new StaticDiscoveryService("ARP", [
                new Device
                {
                    MacAddress = "AA:BB:CC:DD:EE:FF",
                    IpAddress = "192.168.1.10",
                    Vendor = "Dell",
                    DiscoveryMethod = "ARP",
                    LastSeen = observedAt,
                },
            ]),
            new StaticDiscoveryService("Ping", [
                new Device
                {
                    MacAddress = string.Empty,
                    IpAddress = "192.168.1.10",
                    Hostname = "workstation.local",
                    DiscoveryMethod = "Ping",
                    LastSeen = observedAt,
                },
            ]),
        };

        using var cts = new CancellationTokenSource();
        hubContext.OnSend = () => cts.Cancel();

        var worker = CreateWorker(host, repository, scanners, new ScanSettings
        {
            ScanIntervalSeconds = 60,
            OfflineThresholdMinutes = 5,
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => worker.RunUntilCanceledAsync(cts.Token));

        var updated = repository.Get("AA:BB:CC:DD:EE:FF");
        Assert.NotNull(updated);
        Assert.Equal("workstation.local", updated.Hostname);
        Assert.Equal("ARP,Ping", updated.DiscoveryMethod);
        Assert.True(updated.IsOnline);

        var stale = repository.Get("11:22:33:44:55:66");
        Assert.NotNull(stale);
        Assert.False(stale.IsOnline);

        var invocation = Assert.Single(hubContext.Invocations);
        Assert.Equal("DevicesUpdated", invocation.MethodName);
        var snapshot = Assert.IsAssignableFrom<IReadOnlyCollection<Device>>(Assert.Single(invocation.Arguments));
        Assert.Equal(2, snapshot.Count);
        Assert.Contains(snapshot, device => device.MacAddress == "AA:BB:CC:DD:EE:FF" && device.IsOnline);
        Assert.Contains(snapshot, device => device.MacAddress == "11:22:33:44:55:66" && !device.IsOnline);

        await using var scope = host.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LannyDbContext>();
        var persistedStale = await db.Devices.SingleAsync(device => device.MacAddress == "11:22:33:44:55:66");
        Assert.False(persistedStale.IsOnline);
    }

    [Fact]
    public async Task ExecuteAsync_WhenAScannerThrows_ContinuesProcessingRemainingScanners()
    {
        var hubContext = new RecordingHubContext();
        await using var host = await SqliteTestHost.CreateAsync(services =>
        {
            services.AddSingleton<IHubContext<DeviceHub>>(hubContext);
        });

        var repository = host.Services.GetRequiredService<DeviceRepository>();
        var scanners = new IDiscoveryService[]
        {
            new ThrowingDiscoveryService("ARP"),
            new StaticDiscoveryService("mDNS", [
                new Device
                {
                    MacAddress = "00:11:22:33:44:55",
                    IpAddress = "192.168.1.50",
                    DiscoveryMethod = "mDNS",
                    LastSeen = DateTimeOffset.UtcNow,
                },
            ]),
        };

        using var cts = new CancellationTokenSource();
        hubContext.OnSend = () => cts.Cancel();

        var worker = CreateWorker(host, repository, scanners, new ScanSettings
        {
            ScanIntervalSeconds = 60,
            OfflineThresholdMinutes = 5,
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => worker.RunUntilCanceledAsync(cts.Token));

        var device = repository.Get("00:11:22:33:44:55");
        Assert.NotNull(device);
        Assert.Equal("192.168.1.50", device.IpAddress);
        Assert.Single(hubContext.Invocations);
    }

    private static TestWorker CreateWorker(
        SqliteTestHost host,
        DeviceRepository repository,
        IEnumerable<IDiscoveryService> scanners,
        ScanSettings settings)
    {
        return new TestWorker(
            NullLogger<Worker>.Instance,
            repository,
            scanners,
            host.Services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(settings));
    }

    private sealed class TestWorker : Worker
    {
        public TestWorker(
            Microsoft.Extensions.Logging.ILogger<Worker> logger,
            DeviceRepository repository,
            IEnumerable<IDiscoveryService> scanners,
            IServiceScopeFactory scopeFactory,
            IOptions<ScanSettings> settings)
            : base(logger, repository, scanners, scopeFactory, settings)
        {
        }

        public Task RunUntilCanceledAsync(CancellationToken cancellationToken) => ExecuteAsync(cancellationToken);
    }

    private sealed class StaticDiscoveryService : IDiscoveryService
    {
        private readonly IReadOnlyList<Device> _devices;

        public StaticDiscoveryService(string name, IReadOnlyList<Device> devices)
        {
            Name = name;
            _devices = devices;
        }

        public string Name { get; }

        public Task<IReadOnlyList<Device>> ScanAsync(CancellationToken ct) => Task.FromResult(_devices);
    }

    private sealed class ThrowingDiscoveryService : IDiscoveryService
    {
        public ThrowingDiscoveryService(string name)
        {
            Name = name;
        }

        public string Name { get; }

        public Task<IReadOnlyList<Device>> ScanAsync(CancellationToken ct) =>
            throw new InvalidOperationException("boom");
    }

    private sealed class RecordingHubContext : IHubContext<DeviceHub>
    {
        private readonly RecordingClientProxy _clientProxy = new();

        public RecordingHubContext()
        {
            Clients = new RecordingHubClients(_clientProxy);
            Groups = new NoOpGroupManager();
        }

        public Action? OnSend
        {
            get => _clientProxy.OnSend;
            set => _clientProxy.OnSend = value;
        }

        public List<HubInvocation> Invocations => _clientProxy.Invocations;

        public IHubClients Clients { get; }

        public IGroupManager Groups { get; }
    }

    private sealed class RecordingHubClients : IHubClients
    {
        private readonly IClientProxy _clientProxy;

        public RecordingHubClients(IClientProxy clientProxy)
        {
            _clientProxy = clientProxy;
        }

        public IClientProxy All => _clientProxy;

        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => _clientProxy;

        public IClientProxy Client(string connectionId) => _clientProxy;

        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => _clientProxy;

        public IClientProxy Group(string groupName) => _clientProxy;

        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => _clientProxy;

        public IClientProxy Groups(IReadOnlyList<string> groupNames) => _clientProxy;

        public IClientProxy User(string userId) => _clientProxy;

        public IClientProxy Users(IReadOnlyList<string> userIds) => _clientProxy;
    }

    private sealed class RecordingClientProxy : IClientProxy
    {
        public List<HubInvocation> Invocations { get; } = [];

        public Action? OnSend { get; set; }

        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
        {
            Invocations.Add(new HubInvocation(method, args));
            OnSend?.Invoke();
            return Task.CompletedTask;
        }
    }

    private sealed class NoOpGroupManager : IGroupManager
    {
        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed record HubInvocation(string MethodName, object?[] Arguments);
}