using System.Data;
using FluentAssertions;
using MediCare.Data.Context;
using MediCare.Data.UnitOfWork;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MediCare.Tests.Integration;

[Collection("SqlServerIntegration")]
[Trait("Category", "Integration")]
public sealed class SqlServerApplicationLockTests : IClassFixture<SqlServerDatabaseFixture>
{
    private readonly SqlServerDatabaseFixture _fixture;

    public SqlServerApplicationLockTests(SqlServerDatabaseFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task ReminderLock_IsHeldBySqlServerAcrossSeparateConnectionsUntilTransactionEnds()
    {
        var resource = $"ReminderLockIntegration_{Guid.NewGuid():N}";
        await using var firstContext = new ApplicationDbContext(_fixture.Options);
        await using var firstTransaction = await firstContext.Database.BeginTransactionAsync();
        await using var lockHandle = await ApplicationLockManager.AcquireLockAsync(
            firstContext, resource, TimeSpan.FromSeconds(5));

        await using var secondContext = new ApplicationDbContext(_fixture.Options);
        await secondContext.Database.OpenConnectionAsync();
        var blockedResult = await TryAcquireSessionLockAsync(secondContext, resource);
        blockedResult.Should().Be(-1, "a separate SQL connection must see the transaction-owned exclusive lock");

        await firstTransaction.RollbackAsync();
        var acquiredResult = await TryAcquireSessionLockAsync(secondContext, resource);
        acquiredResult.Should().BeGreaterThanOrEqualTo(0);
        await ReleaseSessionLockAsync(secondContext, resource);
    }

    private static async Task<int> TryAcquireSessionLockAsync(ApplicationDbContext context, string resource)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = "DECLARE @result int; EXEC @result = sp_getapplock @Resource = @resource, @LockMode = 'Exclusive', @LockOwner = 'Session', @LockTimeout = 0; SELECT @result;";
        AddResourceParameter(command, resource);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task ReleaseSessionLockAsync(ApplicationDbContext context, string resource)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = "EXEC sp_releaseapplock @Resource = @resource, @LockOwner = 'Session';";
        AddResourceParameter(command, resource);
        await command.ExecuteNonQueryAsync();
    }

    private static void AddResourceParameter(System.Data.Common.DbCommand command, string resource)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@resource";
        parameter.DbType = DbType.String;
        parameter.Value = resource;
        command.Parameters.Add(parameter);
    }
}
