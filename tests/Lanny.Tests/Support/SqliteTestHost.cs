using Lanny.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lanny.Tests.Support;

internal sealed class SqliteTestHost : IAsyncDisposable
{
    private readonly SqliteConnection _connection;

    private SqliteTestHost(SqliteConnection connection, ServiceProvider services)
    {
        _connection = connection;
        Services = services;
    }

    public ServiceProvider Services { get; }

    public static async Task<SqliteTestHost> CreateAsync(Action<IServiceCollection>? configureServices = null)
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        return await CreateAsync(connection, services =>
        {
            services.AddDbContext<LannyDbContext>(options => options.UseSqlite(connection));
            configureServices?.Invoke(services);
        });
    }

    public static async Task<SqliteTestHost> CreateFileBackedAsync(string connectionString, Action<IServiceCollection>? configureServices = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        return await CreateAsync(connection, services =>
        {
            services.AddDbContext<LannyDbContext>(options => options.UseSqlite(connectionString));
            configureServices?.Invoke(services);
        });
    }

    private static async Task<SqliteTestHost> CreateAsync(SqliteConnection connection, Action<IServiceCollection> configureServices)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(configureServices);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<DeviceRepository>();
        configureServices(services);

        var provider = services.BuildServiceProvider();

        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LannyDbContext>();
        await LannyDbSchemaUpdater.EnsureCreatedAndUpdatedAsync(db);

        return new SqliteTestHost(connection, provider);
    }

    public AsyncServiceScope CreateScope() => Services.CreateAsyncScope();

    public async ValueTask DisposeAsync()
    {
        await Services.DisposeAsync();
        await _connection.DisposeAsync();
    }
}