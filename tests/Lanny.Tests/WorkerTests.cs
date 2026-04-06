using Lanny.Data;
using Lanny.Discovery;
using Lanny.Hubs;
using Lanny.Models;
using Lanny.Runtime;
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

    [Fact]
    public async Task ExecuteAsync_WhenSnmpObservationIsAvailable_MergesSnmpMetadataIntoTrackedDevice()
    {
        var hubContext = new RecordingHubContext();
        await using var host = await SqliteTestHost.CreateAsync(services =>
        {
            services.AddSingleton<IHubContext<DeviceHub>>(hubContext);
        });

        var repository = host.Services.GetRequiredService<DeviceRepository>();
        var scanners = new IDiscoveryService[]
        {
            new StaticDiscoveryService("ARP", [
                new Device
                {
                    MacAddress = "AA:BB:CC:DD:EE:01",
                    IpAddress = "192.168.1.10",
                    DiscoveryMethod = "ARP",
                    LastSeen = DateTimeOffset.UtcNow,
                },
            ]),
        };
        var snmpProvider = new StubSnmpMetadataProvider(new Device
        {
            IpAddress = "192.168.1.10",
            Hostname = "core-switch",
            SystemName = "core-switch",
            SystemDescription = "Cisco IOS XE",
            SystemObjectId = "1.3.6.1.4.1.9.1.1208",
            DiscoveryMethod = "SNMP",
            LastSeen = DateTimeOffset.UtcNow,
        });

        using var cts = new CancellationTokenSource();
        hubContext.OnSend = () => cts.Cancel();

        var worker = CreateWorker(host, repository, scanners, new ScanSettings
        {
            ScanIntervalSeconds = 60,
            OfflineThresholdMinutes = 5,
        }, snmpProvider);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => worker.RunUntilCanceledAsync(cts.Token));

        var device = repository.Get("AA:BB:CC:DD:EE:01");
        Assert.NotNull(device);
        Assert.Equal("core-switch", device.Hostname);
        Assert.Equal("core-switch", device.SystemName);
        Assert.Equal("Cisco IOS XE", device.SystemDescription);
        Assert.Equal("1.3.6.1.4.1.9.1.1208", device.SystemObjectId);
        Assert.Equal("ARP,SNMP", device.DiscoveryMethod);
    }

    [Fact]
    public async Task ExecuteAsync_WhenCancellationOccursDuringScan_StopsWithoutBroadcasting()
    {
        var hubContext = new RecordingHubContext();
        await using var host = await SqliteTestHost.CreateAsync(services =>
        {
            services.AddSingleton<IHubContext<DeviceHub>>(hubContext);
        });

        var repository = host.Services.GetRequiredService<DeviceRepository>();
        var scanner = new BlockingDiscoveryService("ARP");
        var worker = CreateWorker(host, repository, [scanner], new ScanSettings
        {
            ScanIntervalSeconds = 60,
            OfflineThresholdMinutes = 5,
        });

        using var cts = new CancellationTokenSource();
        var runTask = worker.RunUntilCanceledAsync(cts.Token);
        await scanner.WaitUntilStartedAsync();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runTask);
        Assert.Empty(hubContext.Invocations);
    }

    [Fact]
    public async Task ExecuteAsync_WhenBroadcastFails_ContinuesToNextCycle()
    {
        var hubContext = new FlakyHubContext(failuresBeforeSuccess: 1);
        await using var host = await SqliteTestHost.CreateAsync(services =>
        {
            services.AddSingleton<IHubContext<DeviceHub>>(hubContext);
        });

        var repository = host.Services.GetRequiredService<DeviceRepository>();
        var scanner = new CountingStaticDiscoveryService("ARP", [
            new Device
            {
                MacAddress = "AA:BB:CC:DD:EE:01",
                IpAddress = "192.168.1.10",
                DiscoveryMethod = "ARP",
                LastSeen = DateTimeOffset.UtcNow,
            },
        ]);

        using var cts = new CancellationTokenSource();
        hubContext.OnSuccessfulSend = () => cts.Cancel();

        var worker = CreateWorker(host, repository, [scanner], new ScanSettings
        {
            ScanIntervalSeconds = 0,
            OfflineThresholdMinutes = 5,
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => worker.RunUntilCanceledAsync(cts.Token));

        Assert.Equal(2, scanner.CallCount);
        Assert.Equal(2, hubContext.SendAttemptCount);
        Assert.Single(hubContext.Invocations);
    }

    private static TestWorker CreateWorker(
        SqliteTestHost host,
        DeviceRepository repository,
        IEnumerable<IDiscoveryService> scanners,
        ScanSettings settings,
        ISnmpMetadataProvider? snmpMetadataProvider = null)
    {
        return new TestWorker(
            NullLogger<Worker>.Instance,
            repository,
            scanners,
            snmpMetadataProvider ?? new StubSnmpMetadataProvider(null),
            new ScanLoopMonitor(),
            host.Services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(settings));
    }

    private sealed class TestWorker : Worker
    {
        public TestWorker(
            Microsoft.Extensions.Logging.ILogger<Worker> logger,
            DeviceRepository repository,
            IEnumerable<IDiscoveryService> scanners,
            ISnmpMetadataProvider snmpMetadataProvider,
            ScanLoopMonitor scanLoopMonitor,
            IServiceScopeFactory scopeFactory,
            IOptions<ScanSettings> settings)
            : base(logger, repository, scanners, snmpMetadataProvider, scanLoopMonitor, scopeFactory, settings)
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

    private sealed class CountingStaticDiscoveryService : IDiscoveryService
    {
        private readonly IReadOnlyList<Device> _devices;

        public CountingStaticDiscoveryService(string name, IReadOnlyList<Device> devices)
        {
            Name = name;
            _devices = devices;
        }

        public int CallCount { get; private set; }

        public string Name { get; }

        public Task<IReadOnlyList<Device>> ScanAsync(CancellationToken ct)
        {
            CallCount++;
            return Task.FromResult(_devices);
        }
    }

    private sealed class BlockingDiscoveryService : IDiscoveryService
    {
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public BlockingDiscoveryService(string name)
        {
            Name = name;
        }

        public string Name { get; }

        public Task WaitUntilStartedAsync() => _started.Task;

        public async Task<IReadOnlyList<Device>> ScanAsync(CancellationToken ct)
        {
            _started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return [];
        }
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

    private sealed class StubSnmpMetadataProvider : ISnmpMetadataProvider
    {
        private readonly Device? _observation;

        public StubSnmpMetadataProvider(Device? observation)
        {
            _observation = observation;
        }

        public Task<Device?> TryGetObservationAsync(Device device, CancellationToken cancellationToken)
        {
            return Task.FromResult(_observation?.IpAddress == device.IpAddress ? _observation : null);
        }
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

    private sealed class FlakyHubContext : IHubContext<DeviceHub>
    {
        private readonly FlakyClientProxy _clientProxy;

        public FlakyHubContext(int failuresBeforeSuccess)
        {
            _clientProxy = new FlakyClientProxy(failuresBeforeSuccess);
            Clients = new RecordingHubClients(_clientProxy);
            Groups = new NoOpGroupManager();
        }

        public Action? OnSuccessfulSend
        {
            get => _clientProxy.OnSuccessfulSend;
            set => _clientProxy.OnSuccessfulSend = value;
        }

        public List<HubInvocation> Invocations => _clientProxy.Invocations;

        public int SendAttemptCount => _clientProxy.SendAttemptCount;

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

    private sealed class FlakyClientProxy : IClientProxy
    {
        private int _remainingFailures;

        public FlakyClientProxy(int failuresBeforeSuccess)
        {
            _remainingFailures = failuresBeforeSuccess;
        }

        public List<HubInvocation> Invocations { get; } = [];

        public Action? OnSuccessfulSend { get; set; }

        public int SendAttemptCount { get; private set; }

        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
        {
            SendAttemptCount++;

            if (_remainingFailures > 0)
            {
                _remainingFailures--;
                throw new InvalidOperationException("hub send failed");
            }

            Invocations.Add(new HubInvocation(method, args));
            OnSuccessfulSend?.Invoke();
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