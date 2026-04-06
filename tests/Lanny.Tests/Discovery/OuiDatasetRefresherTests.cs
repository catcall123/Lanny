using Lanny.Discovery;
using Lanny.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Lanny.Tests.Discovery;

public class OuiDatasetRefresherTests
{
    [Fact]
    public async Task RefreshAsync_DownloadsOfficialCsvSourcesAndWritesNormalizedDataset()
    {
        var datasetPath = Path.Combine(Path.GetTempPath(), $"lanny-oui-{Guid.NewGuid():N}.csv");
        var client = new RecordingOuiDatasetHttpClient(new Dictionary<string, string[]>
        {
            ["https://example.test/oui.csv"] =
            [
                "Registry,Assignment,Organization Name,Organization Address",
                "MA-L,7085C2,Apple Inc.,One Apple Park Way Cupertino CA US 95014",
            ],
            ["https://example.test/cid.csv"] =
            [
                "Registry,Assignment,Organization Name,Organization Address",
                "CID,10D31A,Contoso Labs,1 Example Way Redmond WA US 98052",
            ],
        });
        var refresher = new OuiDatasetRefresher(
            client,
            Options.Create(new OuiDatasetOptions
            {
                DatasetPath = datasetPath,
                SourceUrls =
                [
                    "https://example.test/oui.csv",
                    "https://example.test/cid.csv",
                ],
            }),
            NullLogger<OuiDatasetRefresher>.Instance);

        try
        {
            await refresher.RefreshAsync(CancellationToken.None);

            var lines = await File.ReadAllLinesAsync(datasetPath);

            Assert.Contains("70:85:C2,Apple Inc.", lines);
            Assert.Contains("10:D3:1A,Contoso Labs", lines);
        }
        finally
        {
            TryDelete(datasetPath);
        }
    }

    [Fact]
    public async Task Resolve_WhenDatasetFileChanges_ReloadsWithoutRestart()
    {
        var datasetPath = Path.Combine(Path.GetTempPath(), $"lanny-oui-live-{Guid.NewGuid():N}.csv");
        var originalPath = Environment.GetEnvironmentVariable("LANNY_OUI_DATASET_PATH");

        try
        {
            await File.WriteAllLinesAsync(datasetPath,
            [
                "# Prefix,Vendor",
                "70:85:C2,Apple",
            ]);
            File.SetLastWriteTimeUtc(datasetPath, DateTime.UtcNow.AddMinutes(-1));
            Environment.SetEnvironmentVariable("LANNY_OUI_DATASET_PATH", datasetPath);

            Assert.Equal("Apple", OuiLookup.Resolve("70:85:C2:00:11:22"));

            await File.WriteAllLinesAsync(datasetPath,
            [
                "# Prefix,Vendor",
                "70:85:C2,Contoso",
            ]);
            File.SetLastWriteTimeUtc(datasetPath, DateTime.UtcNow.AddMinutes(1));

            Assert.Equal("Contoso", OuiLookup.Resolve("70:85:C2:00:11:22"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("LANNY_OUI_DATASET_PATH", originalPath);
            TryDelete(datasetPath);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
        }
    }

    private sealed class RecordingOuiDatasetHttpClient : IOuiDatasetHttpClient
    {
        private readonly IReadOnlyDictionary<string, string[]> _responses;

        public RecordingOuiDatasetHttpClient(IReadOnlyDictionary<string, string[]> responses)
        {
            _responses = responses;
        }

        public Task<IReadOnlyList<string>> GetLinesAsync(string url, CancellationToken cancellationToken)
        {
            if (!_responses.TryGetValue(url, out var lines))
                throw new InvalidOperationException($"No dataset response configured for {url}");

            return Task.FromResult<IReadOnlyList<string>>(lines);
        }
    }
}