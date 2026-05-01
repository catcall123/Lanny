using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Lanny.Data;

internal static class SqliteBusyRetryPolicy
{
    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromMilliseconds(100),
        TimeSpan.FromMilliseconds(200),
        TimeSpan.FromMilliseconds(400),
    ];

    public static Task ExecuteAsync(
        Func<CancellationToken, Task> operation,
        ILogger logger,
        string operationName,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationName);

        return ExecuteCoreAsync<object?>(
            async token =>
            {
                await operation(token);
                return null;
            },
            logger,
            operationName,
            cancellationToken);
    }

    public static Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        ILogger logger,
        string operationName,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationName);

        return ExecuteCoreAsync(operation, logger, operationName, cancellationToken);
    }

    private static async Task<T> ExecuteCoreAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        ILogger logger,
        string operationName,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                return await operation(cancellationToken);
            }
            catch (Exception ex) when (attempt < RetryDelays.Length && IsTransientSqliteLock(ex))
            {
                logger.LogWarning("Transient SQLite lock while attempting to {OperationName}; retry {Attempt} of {MaxAttempts}: {Message}", operationName, attempt + 1, RetryDelays.Length, ex.Message);
                await Task.Delay(RetryDelays[attempt], cancellationToken);
            }
        }
    }

    private static bool IsTransientSqliteLock(Exception exception)
    {
        if (exception is DbUpdateException dbUpdateException && dbUpdateException.InnerException is not null)
            return IsTransientSqliteLock(dbUpdateException.InnerException);

        return exception is SqliteException sqliteException && sqliteException.SqliteErrorCode is 5 or 6;
    }
}
