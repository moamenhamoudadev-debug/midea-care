using MediCare.Data.Context;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MediCare.Tests.Integration;

public class SqlServerDatabaseFixture : IAsyncLifetime
{
    private static readonly SemaphoreSlim _initLock = new(1, 1);
    private static bool _isInitialized = false;

    public string ConnectionString { get; }
    public DbContextOptions<ApplicationDbContext> Options { get; }

    public SqlServerDatabaseFixture()
    {
        ConnectionString = SqlServerTestDatabaseSafety.ReadBaseConnectionString();

        Options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;
    }

    public async Task InitializeAsync()
    {
        if (_isInitialized) return;

        await _initLock.WaitAsync();
        try
        {
            if (_isInitialized) return;

            const int maxRetries = 5;
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    using var context = new ApplicationDbContext(Options);
                    await context.Database.MigrateAsync();
                    _isInitialized = true;
                    break;
                }
                catch (Exception) when (attempt < maxRetries)
                {
                    await Task.Delay(1000 * attempt);
                }
            }
        }
        finally
        {
            _initLock.Release();
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

[CollectionDefinition("SqlServerIntegration", DisableParallelization = true)]
public class SqlServerIntegrationCollection : ICollectionFixture<SqlServerDatabaseFixture>
{
    // This class has no code, and is never created. Its purpose is simply
    // to be the place to apply [CollectionDefinition] and all the
    // ICollectionFixture<> interfaces.
}
