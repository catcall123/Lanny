namespace Lanny.Discovery;

public interface IOuiDatasetHttpClient
{
    Task<IReadOnlyList<string>> GetLinesAsync(string url, CancellationToken cancellationToken);
}