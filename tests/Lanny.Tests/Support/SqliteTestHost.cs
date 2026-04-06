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

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<LannyDbContext>(options => options.UseSqlite(connection));
        services.AddSingleton<DeviceRepository>();

        configureServices?.Invoke(services);

        var provider = services.BuildServiceProvider();

        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LannyDbContext>();
        await db.Database.EnsureCreatedAsync();

        return new SqliteTestHost(connection, provider);
    }

    public AsyncServiceScope CreateScope() => Services.CreateAsyncScope();

    public async ValueTask DisposeAsync()
    {
        await Services.DisposeAsync();
        await _connection.DisposeAsync();
    }
}