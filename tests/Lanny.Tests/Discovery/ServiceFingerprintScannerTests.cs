using Lanny.Discovery;
using Lanny.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Lanny.Tests.Discovery;

public class ServiceFingerprintScannerTests
{
    [Fact]
    public async Task ScanAsync_WhenFingerprintingIsDisabled_ReturnsNoObservations()
    {
        var probe = new RecordingProbe();
        var scanner = new ServiceFingerprintScanner(
            [probe],
            NullLogger<ServiceFingerprintScanner>.Instance,
            Options.Create(new ScanSettings
            {
                EnableServiceFingerprinting = false,
                FingerprintMaxConcurrency = 1,
                FingerprintTimeoutMs = 250,
            }));

        var observations = await scanner.ScanAsync(
            [new Device { MacAddress = "AA:BB:CC:DD:EE:01", IpAddress = "192.168.1.10" }],
            CancellationToken.None);

        Assert.Empty(observations);
        Assert.Equal(0, probe.CallCount);
    }

    [Fact]
    public async Task ScanAsync_WhenProbesReturnMetadata_MergesProtocolObservationsIntoSingleResult()
    {
        var scanner = new ServiceFingerprintScanner(
            [
                new StaticProbe(new Device
                {
                    HttpTitle = "Router Admin",
                    HttpHeaders = new Dictionary<string, string>
                    {
                        ["server"] = "nginx",
                    },
                    DiscoveryMethod = "HTTP",
                }),
                new StaticProbe(new Device
                {
                    TlsCertificateSubject = "CN=router.local",
                    TlsSubjectAlternativeNames = ["router.local", "router"],
                    DiscoveryMethod = "TLS",
                }),
                new StaticProbe(new Device
                {
                    SshBanner = "SSH-2.0-OpenSSH_9.6",
                    DiscoveryMethod = "SSH",
                }),
            ],
            NullLogger<ServiceFingerprintScanner>.Instance,
            Options.Create(new ScanSettings
            {
                EnableServiceFingerprinting = true,
                FingerprintMaxConcurrency = 2,
                FingerprintTimeoutMs = 250,
            }));

        var observations = await scanner.ScanAsync(
            [new Device { MacAddress = "AA:BB:CC:DD:EE:01", IpAddress = "192.168.1.10", Hostname = "mdns-name" }],
            CancellationToken.None);

        var observation = Assert.Single(observations);
        Assert.Equal("192.168.1.10", observation.IpAddress);
        Assert.Equal("Router Admin", observation.HttpTitle);
        Assert.Equal("nginx", observation.HttpHeaders!["server"]);
        Assert.Equal("CN=router.local", observation.TlsCertificateSubject);
        Assert.Equal(["router.local", "router"], observation.TlsSubjectAlternativeNames);
        Assert.Equal("SSH-2.0-OpenSSH_9.6", observation.SshBanner);
        Assert.Equal("HTTP,TLS,SSH", observation.DiscoveryMethod);
    }

    [Fact]
    public async Task ScanAsync_WhenProbeFails_ContinuesWithOtherSuccessfulProbes()
    {
        var scanner = new ServiceFingerprintScanner(
            [
                new ThrowingProbe(new TimeoutException("timed out")),
                new StaticProbe(new Device
                {
                    SshBanner = "SSH-2.0-OpenSSH_9.6",
                    DiscoveryMethod = "SSH",
                }),
            ],
            NullLogger<ServiceFingerprintScanner>.Instance,
            Options.Create(new ScanSettings
            {
                EnableServiceFingerprinting = true,
                FingerprintMaxConcurrency = 2,
                FingerprintTimeoutMs = 250,
            }));

        var observations = await scanner.ScanAsync(
            [new Device { MacAddress = "AA:BB:CC:DD:EE:01", IpAddress = "192.168.1.10" }],
            CancellationToken.None);

        var observation = Assert.Single(observations);
        Assert.Equal("SSH-2.0-OpenSSH_9.6", observation.SshBanner);
        Assert.Equal("SSH", observation.DiscoveryMethod);
    }

    [Fact]
    public async Task ScanAsync_HonorsConfiguredConcurrencyLimit()
    {
        var probe = new ConcurrencyTrackingProbe();
        var scanner = new ServiceFingerprintScanner(
            [probe],
            NullLogger<ServiceFingerprintScanner>.Instance,
            Options.Create(new ScanSettings
            {
                EnableServiceFingerprinting = true,
                FingerprintMaxConcurrency = 1,
                FingerprintTimeoutMs = 1000,
            }));

        var scanTask = scanner.ScanAsync(
            [
                new Device { MacAddress = "AA:BB:CC:DD:EE:01", IpAddress = "192.168.1.10" },
                new Device { MacAddress = "AA:BB:CC:DD:EE:02", IpAddress = "192.168.1.11" },
            ],
            CancellationToken.None);

        await probe.WaitForFirstCallAsync();
        probe.ReleaseOne();
        await probe.WaitForSecondCallAsync();
        probe.ReleaseOne();

        await scanTask;

        Assert.Equal(1, probe.MaxConcurrentCalls);
    }

    private sealed class RecordingProbe : IServiceFingerprintProbe
    {
        public int CallCount { get; private set; }

        public Task<Device?> ProbeAsync(Device device, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult<Device?>(null);
        }
    }

    private sealed class StaticProbe : IServiceFingerprintProbe
    {
        private readonly Device _observation;

        public StaticProbe(Device observation)
        {
            _observation = observation;
        }

        public Task<Device?> ProbeAsync(Device device, CancellationToken cancellationToken)
        {
            return Task.FromResult<Device?>(new Device
            {
                IpAddress = device.IpAddress,
                HttpTitle = _observation.HttpTitle,
                HttpHeaders = _observation.HttpHeaders,
                TlsCertificateSubject = _observation.TlsCertificateSubject,
                TlsSubjectAlternativeNames = _observation.TlsSubjectAlternativeNames,
                SshBanner = _observation.SshBanner,
                DiscoveryMethod = _observation.DiscoveryMethod,
            });
        }
    }

    private sealed class ThrowingProbe : IServiceFingerprintProbe
    {
        private readonly Exception _exception;

        public ThrowingProbe(Exception exception)
        {
            _exception = exception;
        }

        public Task<Device?> ProbeAsync(Device device, CancellationToken cancellationToken)
        {
            throw _exception;
        }
    }

    private sealed class ConcurrencyTrackingProbe : IServiceFingerprintProbe
    {
        private readonly TaskCompletionSource _firstCallStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _secondCallStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Queue<TaskCompletionSource> _releases = new();
        private int _callCount;
        private int _concurrentCalls;

        public int MaxConcurrentCalls { get; private set; }

        public Task WaitForFirstCallAsync() => _firstCallStarted.Task;

        public Task WaitForSecondCallAsync() => _secondCallStarted.Task;

        public void ReleaseOne()
        {
            _releases.Dequeue().TrySetResult();
        }

        public async Task<Device?> ProbeAsync(Device device, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _callCount);
            var concurrent = Interlocked.Increment(ref _concurrentCalls);
            MaxConcurrentCalls = Math.Max(MaxConcurrentCalls, concurrent);

            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_releases)
            {
                _releases.Enqueue(release);
            }

            if (_callCount == 1)
                _firstCallStarted.TrySetResult();
            else if (_callCount == 2)
                _secondCallStarted.TrySetResult();

            await release.Task.WaitAsync(cancellationToken);
            Interlocked.Decrement(ref _concurrentCalls);
            return null;
        }
    }
}