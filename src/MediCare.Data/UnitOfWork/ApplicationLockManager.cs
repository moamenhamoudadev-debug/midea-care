using System.Collections.Concurrent;
using System.Data;
using MediCare.Data.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace MediCare.Data.UnitOfWork;

public sealed class NullAsyncDisposable : IAsyncDisposable, IDisposable
{
    public static readonly NullAsyncDisposable Instance = new();
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    public void Dispose() { }
}

public sealed class NullDatabaseTransaction : IDatabaseTransaction
{
    public static readonly NullDatabaseTransaction Instance = new();
    public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task RollbackAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    public void Dispose() { }
}

public sealed class InMemoryDatabaseTransaction : IDatabaseTransaction
{
    public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task RollbackAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    public void Dispose() { }
}

public sealed class EfDatabaseTransaction : IDatabaseTransaction
{
    private readonly IDbContextTransaction _transaction;

    public EfDatabaseTransaction(IDbContextTransaction transaction)
    {
        _transaction = transaction;
    }

    public Task CommitAsync(CancellationToken cancellationToken = default)
    {
        return _transaction.CommitAsync(cancellationToken);
    }

    public Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        return _transaction.RollbackAsync(cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        return _transaction.DisposeAsync();
    }

    public void Dispose()
    {
        _transaction.Dispose();
    }
}

public static class ApplicationLockManager
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public static Task<IAsyncDisposable> AcquireLockAsync(
        ApplicationDbContext context,
        string lockKey,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        return AcquireLocksAsync(context, new[] { lockKey }, timeout, cancellationToken);
    }

    public static async Task<IAsyncDisposable> AcquireLocksAsync(
        ApplicationDbContext context,
        IEnumerable<string> lockKeys,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var sortedKeys = lockKeys
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();

        if (sortedKeys.Count == 0)
        {
            return NullAsyncDisposable.Instance;
        }

        var acquiredSemaphores = new List<SemaphoreSlim>(sortedKeys.Count);

        try
        {
            foreach (var key in sortedKeys)
            {
                var semaphore = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
                var acquired = await semaphore.WaitAsync(timeout, cancellationToken);
                if (!acquired)
                {
                    throw new TimeoutException($"Timed out waiting to acquire application lock for key: {key}");
                }
                acquiredSemaphores.Add(semaphore);

                if (context.Database.IsSqlServer() && context.Database.CurrentTransaction != null)
                {
                    var timeoutMs = (int)Math.Max(0, timeout.TotalMilliseconds);
                    await context.Database.ExecuteSqlInterpolatedAsync(
                        $"DECLARE @res int; EXEC @res = sp_getapplock @Resource = {key}, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = {timeoutMs}; IF @res < 0 THROW 50001, 'Failed to acquire application lock in SQL Server', 1;",
                        cancellationToken);
                }
            }

            return new CompositeReleaser(acquiredSemaphores);
        }
        catch
        {
            for (int i = acquiredSemaphores.Count - 1; i >= 0; i--)
            {
                acquiredSemaphores[i].Release();
            }
            throw;
        }
    }

    private sealed class CompositeReleaser : IAsyncDisposable, IDisposable
    {
        private readonly List<SemaphoreSlim> _semaphores;
        private int _disposed;

        public CompositeReleaser(List<SemaphoreSlim> semaphores)
        {
            _semaphores = semaphores;
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                for (int i = _semaphores.Count - 1; i >= 0; i--)
                {
                    _semaphores[i].Release();
                }
            }
        }
    }
}
