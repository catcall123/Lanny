using System.Collections.Frozen;
using Lanny.Models;
using Microsoft.Extensions.Options;

namespace Lanny.Discovery;

public class OuiDatasetRefresher
{
    private readonly IOuiDatasetHttpClient _httpClient;
    private readonly ILogger<OuiDatasetRefresher> _logger;
    private readonly OuiDatasetOptions _options;

    public OuiDatasetRefresher(
        IOuiDatasetHttpClient httpClient,
        IOptions<OuiDatasetOptions> options,
        ILogger<OuiDatasetRefresher> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        var datasetPath = ResolveDatasetPath();
        var sourceUrls = _options.SourceUrls.Where(url => !string.IsNullOrWhiteSpace(url)).ToList();
        if (sourceUrls.Count == 0)
            throw new InvalidOperationException("At least one OUI dataset source URL must be configured.");

        var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var sourceUrl in sourceUrls)
        {
            var lines = await _httpClient.GetLinesAsync(sourceUrl, cancellationToken);
            MergeDataset(merged, OuiVendorDatasetParser.ParseIeeeCsvLines(lines));
        }

        if (merged.Count == 0)
            throw new InvalidOperationException("No OUI vendor entries were downloaded from the configured sources.");

        Directory.CreateDirectory(Path.GetDirectoryName(datasetPath)!);

        var tempPath = datasetPath + ".tmp";
        var outputLines = new List<string>(merged.Count + 1)
        {
            "# Prefix,Vendor",
        };
        outputLines.AddRange(merged.OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase).Select(entry => $"{entry.Key},{entry.Value}"));

        await File.WriteAllLinesAsync(tempPath, outputLines, cancellationToken);
        File.Move(tempPath, datasetPath, true);
        _logger.LogInformation("Refreshed OUI vendor dataset with {Count} entries", merged.Count);
    }

    public bool ShouldRefresh()
    {
        var datasetPath = ResolveDatasetPath();
        if (!File.Exists(datasetPath))
            return true;

        var refreshIntervalHours = _options.RefreshIntervalHours > 0 ? _options.RefreshIntervalHours : 168;
        var lastWriteUtc = File.GetLastWriteTimeUtc(datasetPath);
        return DateTime.UtcNow - lastWriteUtc >= TimeSpan.FromHours(refreshIntervalHours);
    }

    private string ResolveDatasetPath()
    {
        return string.IsNullOrWhiteSpace(_options.DatasetPath)
            ? Path.Combine(AppContext.BaseDirectory, "Data", "oui-prefixes.csv")
            : _options.DatasetPath;
    }

    private static void MergeDataset(Dictionary<string, string> target, FrozenDictionary<string, string> source)
    {
        foreach (var (prefix, vendor) in source)
        {
            target[prefix] = vendor;
        }
    }
}