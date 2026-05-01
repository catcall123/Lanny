using System.Net.Http.Headers;
using Lanny.Models;
using Microsoft.Extensions.Options;

namespace Lanny.Discovery;

public class HttpFingerprintProbe : IServiceFingerprintProbe
{
    private static readonly (string Scheme, int Port)[] Endpoints = [("https", 443), ("http", 80)];
    private readonly ScanSettings _settings;

    public HttpFingerprintProbe(ILogger<HttpFingerprintProbe> logger, IOptions<ScanSettings> settings)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _settings = settings?.Value ?? throw new ArgumentNullException(nameof(settings));
    }

    public async Task<Device?> ProbeAsync(Device device, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(device);

        if (string.IsNullOrWhiteSpace(device.IpAddress))
            return null;

        Device? observation = null;
        foreach (var (scheme, port) in Endpoints)
        {
            try
            {
                using var handler = new HttpClientHandler();
                if (scheme == "https")
                    handler.ServerCertificateCustomValidationCallback = static (_, _, _, _) => true;

                using var client = new HttpClient(handler)
                {
                    Timeout = Timeout.InfiniteTimeSpan,
                };
                using var request = new HttpRequestMessage(HttpMethod.Get, $"{scheme}://{device.IpAddress}:{port}/");
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

                var body = await ReadBodyPreviewAsync(response.Content, cancellationToken);
                var metadata = HttpFingerprintParser.Parse(CollectHeaders(response.Headers, response.Content.Headers), body);
                if (string.IsNullOrWhiteSpace(metadata.Title) && metadata.Headers.Count == 0)
                    continue;

                observation ??= new Device
                {
                    IpAddress = device.IpAddress,
                    DiscoveryMethod = "HTTP",
                };

                observation.HttpTitle ??= metadata.Title;
                if (observation.HttpHeaders is null)
                    observation.HttpHeaders = metadata.Headers;
                else
                    MergeHeaders(observation.HttpHeaders, metadata.Headers);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
            }
        }

        return observation;
    }

    private static Dictionary<string, string> CollectHeaders(HttpResponseHeaders responseHeaders, HttpContentHeaders contentHeaders)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in responseHeaders)
            headers[header.Key] = string.Join(", ", header.Value);

        foreach (var header in contentHeaders)
            headers[header.Key] = string.Join(", ", header.Value);

        return headers;
    }

    private static void MergeHeaders(Dictionary<string, string> target, IReadOnlyDictionary<string, string> source)
    {
        foreach (var (key, value) in source)
        {
            target.TryAdd(key, value);
        }
    }

    private async Task<string?> ReadBodyPreviewAsync(HttpContent content, CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);
        var buffer = new char[2048];
        var read = await reader.ReadBlockAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
        return read == 0 ? null : new string(buffer, 0, read);
    }
}
