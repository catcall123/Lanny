using Lanny.Models;
using Microsoft.Extensions.Options;

namespace Lanny.Discovery;

public class OuiDatasetRefreshService : BackgroundService
{
    private readonly OuiDatasetRefresher _refresher;
    private readonly ILogger<OuiDatasetRefreshService> _logger;
    private readonly OuiDatasetOptions _options;

    public OuiDatasetRefreshService(
        OuiDatasetRefresher refresher,
        IOptions<OuiDatasetOptions> options,
        ILogger<OuiDatasetRefreshService> logger)
    {
        _refresher = refresher ?? throw new ArgumentNullException(nameof(refresher));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_options.RefreshOnStartup && _refresher.ShouldRefresh())
            await TryRefreshAsync(stoppingToken);

        var refreshIntervalHours = _options.RefreshIntervalHours > 0 ? _options.RefreshIntervalHours : 168;
        using var timer = new PeriodicTimer(TimeSpan.FromHours(refreshIntervalHours));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await TryRefreshAsync(stoppingToken);
        }
    }

    private async Task TryRefreshAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _refresher.RefreshAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to refresh the OUI vendor dataset; continuing with the existing dataset");
        }
    }
}