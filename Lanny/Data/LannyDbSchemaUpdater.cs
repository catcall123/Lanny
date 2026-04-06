using Microsoft.EntityFrameworkCore;

namespace Lanny.Data;

public static class LannyDbSchemaUpdater
{
    public static async Task EnsureCreatedAndUpdatedAsync(LannyDbContext db, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);

        await db.Database.EnsureCreatedAsync(cancellationToken);

        var existingColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA table_info('Devices');";

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                existingColumns.Add(reader.GetString(1));
            }
        }

        if (!existingColumns.Contains("SystemName"))
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"Devices\" ADD COLUMN \"SystemName\" TEXT NULL;", cancellationToken);

        if (!existingColumns.Contains("SystemDescription"))
        {
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE \"Devices\" ADD COLUMN \"SystemDescription\" TEXT NULL;",
                cancellationToken);
        }

        if (!existingColumns.Contains("SystemObjectId"))
        {
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE \"Devices\" ADD COLUMN \"SystemObjectId\" TEXT NULL;",
                cancellationToken);
        }

        if (!existingColumns.Contains("SystemUptime"))
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"Devices\" ADD COLUMN \"SystemUptime\" INTEGER NULL;", cancellationToken);

        if (!existingColumns.Contains("InterfaceCount"))
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"Devices\" ADD COLUMN \"InterfaceCount\" INTEGER NULL;", cancellationToken);
    }
}