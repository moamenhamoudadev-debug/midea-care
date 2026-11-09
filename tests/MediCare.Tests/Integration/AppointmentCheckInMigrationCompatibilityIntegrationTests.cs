using FluentAssertions;
using MediCare.Data.Context;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace MediCare.Tests.Integration;

[Trait("Category", "Integration")]
[Collection("SqlServerIntegration")]
public class AppointmentCheckInMigrationCompatibilityIntegrationTests
{
    private readonly string _baseConnectionString;

    public AppointmentCheckInMigrationCompatibilityIntegrationTests(SqlServerDatabaseFixture fixture)
    {
        _baseConnectionString = fixture.ConnectionString;
    }

    private (ApplicationDbContext Context, string DbName) CreateIsolatedContext()
    {
        var databaseName = $"MediCare_MigTest_{Guid.NewGuid():N}";
        var safeConnectionString = SqlServerTestDatabaseSafety.ForGeneratedDatabase(_baseConnectionString, databaseName);
        var builder = new SqlConnectionStringBuilder(safeConnectionString) { ConnectTimeout = 30 };

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(builder.ConnectionString)
            .Options;

        return (new ApplicationDbContext(options), builder.InitialCatalog);
    }

    private static async Task<bool> ColumnExistsAsync(ApplicationDbContext context, string tableName, string columnName)
    {
        var count = await context.Database
            .SqlQueryRaw<int>(
                "SELECT COUNT(1) AS Value FROM sys.columns c INNER JOIN sys.tables t ON c.object_id = t.object_id WHERE t.name = {0} AND c.name = {1}",
                tableName,
                columnName)
            .SingleAsync();

        return count > 0;
    }

    private static async Task<bool> MigrationRecordedAsync(ApplicationDbContext context, string migrationId)
    {
        var count = await context.Database
            .SqlQueryRaw<int>(
                "SELECT COUNT(1) AS Value FROM [__EFMigrationsHistory] WHERE [MigrationId] = {0}",
                migrationId)
            .SingleAsync();

        return count > 0;
    }

    [Fact]
    public async Task LegacyDevelopState_WhenMigrateAsyncRuns_SucceedsPreservesColumnAndRecordsMigration()
    {
        // Target legacy scenario:
        // 1. Appointments.CheckedInAt column already exists.
        // 2. __EFMigrationsHistory contains 20261012163335_AddAppointmentCheckedInAt.
        // 3. __EFMigrationsHistory does NOT contain 20261011130000_AddAppointmentCheckIn.
        var (context, dbName) = CreateIsolatedContext();
        try
        {
            var migrator = context.Database.GetService<IMigrator>();

            // Setup baseline up to the migration immediately preceding the check-in branches
            await migrator.MigrateAsync("20261009210023_AddAppointmentPaymentDetailsAndBackfill");

            // Simulate the develop branch state: column exists and develop migration is recorded
            await context.Database.ExecuteSqlRawAsync(
                "IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[Appointments]') AND name = N'CheckedInAt') " +
                "ALTER TABLE [Appointments] ADD [CheckedInAt] datetime2 NULL;");

            await context.Database.ExecuteSqlRawAsync(
                "INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion]) " +
                "VALUES (N'20261012163335_AddAppointmentCheckedInAt', N'8.0.13');");

            // Verify preconditions
            (await ColumnExistsAsync(context, "Appointments", "CheckedInAt")).Should().BeTrue();
            (await MigrationRecordedAsync(context, "20261012163335_AddAppointmentCheckedInAt")).Should().BeTrue();
            (await MigrationRecordedAsync(context, "20261011130000_AddAppointmentCheckIn")).Should().BeFalse();

            // Act: Migrate to latest reconciled state
            var act = () => context.Database.MigrateAsync();
            await act.Should().NotThrowAsync("MigrateAsync must succeed when column already exists from a prior develop branch migration");

            // Assert: Column preserved and both migrations recorded
            (await ColumnExistsAsync(context, "Appointments", "CheckedInAt")).Should().BeTrue();
            (await MigrationRecordedAsync(context, "20261011130000_AddAppointmentCheckIn")).Should().BeTrue();
            (await MigrationRecordedAsync(context, "20261012163335_AddAppointmentCheckedInAt")).Should().BeTrue();
        }
        finally
        {
            SqlServerTestDatabaseSafety.AssertSafeDatabase(context.Database.GetConnectionString()!, dbName);
            await context.Database.EnsureDeletedAsync();
            await context.DisposeAsync();
        }
    }

    [Fact]
    public async Task LegacyDevelopState_AfterMigrationForward_RollbackOneMigrationPreservesColumn_AndRollbackPastBothDropsColumn()
    {
        // Target scenario:
        // Starting from the legacy develop state (CheckedInAt exists, 20261012163335 recorded, 20261011130000 absent),
        // the database is reconciled forward.
        // Then:
        // 1. Rollback 1 migration (to 20261011130000): column must NOT be dropped because 20261011130000 still requires it.
        // 2. Rollback past both migrations (to 20261009210023): column must be cleanly dropped without double-drop error.
        var (context, dbName) = CreateIsolatedContext();
        try
        {
            var migrator = context.Database.GetService<IMigrator>();
            await migrator.MigrateAsync("20261009210023_AddAppointmentPaymentDetailsAndBackfill");

            await context.Database.ExecuteSqlRawAsync(
                "IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[Appointments]') AND name = N'CheckedInAt') " +
                "ALTER TABLE [Appointments] ADD [CheckedInAt] datetime2 NULL;");

            await context.Database.ExecuteSqlRawAsync(
                "INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion]) " +
                "VALUES (N'20261012163335_AddAppointmentCheckedInAt', N'8.0.13');");

            // Verify initial legacy state
            (await ColumnExistsAsync(context, "Appointments", "CheckedInAt")).Should().BeTrue();
            (await MigrationRecordedAsync(context, "20261012163335_AddAppointmentCheckedInAt")).Should().BeTrue();
            (await MigrationRecordedAsync(context, "20261011130000_AddAppointmentCheckIn")).Should().BeFalse();

            // Reconcile forward
            await context.Database.MigrateAsync();
            (await MigrationRecordedAsync(context, "20261011130000_AddAppointmentCheckIn")).Should().BeTrue();
            (await MigrationRecordedAsync(context, "20261012163335_AddAppointmentCheckedInAt")).Should().BeTrue();
            (await ColumnExistsAsync(context, "Appointments", "CheckedInAt")).Should().BeTrue();

            // Act 1: Rollback ONE migration (to 20261011130000_AddAppointmentCheckIn)
            await migrator.MigrateAsync("20261011130000_AddAppointmentCheckIn");

            // Assert 1:
            // - 20261012163335 is reverted from __EFMigrationsHistory
            // - 20261011130000 remains in __EFMigrationsHistory
            // - CheckedInAt MUST NOT be dropped because 20261011130000 is still active
            (await MigrationRecordedAsync(context, "20261012163335_AddAppointmentCheckedInAt")).Should().BeFalse();
            (await MigrationRecordedAsync(context, "20261011130000_AddAppointmentCheckIn")).Should().BeTrue();
            (await ColumnExistsAsync(context, "Appointments", "CheckedInAt")).Should().BeTrue(
                "CheckedInAt column must NOT be accidentally dropped while an applied migration (20261011130000) still requires it");

            // Act 2: Rollback past BOTH check-in migrations (to 20261009210023_AddAppointmentPaymentDetailsAndBackfill)
            await migrator.MigrateAsync("20261009210023_AddAppointmentPaymentDetailsAndBackfill");

            // Assert 2:
            // - Neither check-in migration is in __EFMigrationsHistory
            // - CheckedInAt is cleanly dropped without double-drop error
            (await MigrationRecordedAsync(context, "20261011130000_AddAppointmentCheckIn")).Should().BeFalse();
            (await MigrationRecordedAsync(context, "20261012163335_AddAppointmentCheckedInAt")).Should().BeFalse();
            (await ColumnExistsAsync(context, "Appointments", "CheckedInAt")).Should().BeFalse(
                "CheckedInAt column must be cleanly dropped when rolling back past all check-in migrations");
        }
        finally
        {
            SqlServerTestDatabaseSafety.AssertSafeDatabase(context.Database.GetConnectionString()!, dbName);
            await context.Database.EnsureDeletedAsync();
            await context.DisposeAsync();
        }
    }

    [Fact]
    public async Task LegacyDevelopState_DirectRollbackToBeforeCheckIn_DropsColumnSafelyWithoutDoubleDrop()
    {
        // Target scenario:
        // Rolling back directly from the un-reconciled legacy develop state (where 20261011130000 was never applied)
        // to before check-in migrations (20261009210023).
        // 20261012163335.Down() must safely drop CheckedInAt because 20261011130000 is not in history.
        var (context, dbName) = CreateIsolatedContext();
        try
        {
            var migrator = context.Database.GetService<IMigrator>();
            await migrator.MigrateAsync("20261009210023_AddAppointmentPaymentDetailsAndBackfill");

            await context.Database.ExecuteSqlRawAsync(
                "IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[Appointments]') AND name = N'CheckedInAt') " +
                "ALTER TABLE [Appointments] ADD [CheckedInAt] datetime2 NULL;");

            await context.Database.ExecuteSqlRawAsync(
                "INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion]) " +
                "VALUES (N'20261012163335_AddAppointmentCheckedInAt', N'8.0.13');");

            // Verify initial legacy state
            (await ColumnExistsAsync(context, "Appointments", "CheckedInAt")).Should().BeTrue();
            (await MigrationRecordedAsync(context, "20261012163335_AddAppointmentCheckedInAt")).Should().BeTrue();
            (await MigrationRecordedAsync(context, "20261011130000_AddAppointmentCheckIn")).Should().BeFalse();

            // Act: Direct rollback to before check-in migrations
            var act = () => migrator.MigrateAsync("20261009210023_AddAppointmentPaymentDetailsAndBackfill");
            await act.Should().NotThrowAsync("Direct rollback from legacy develop state must succeed cleanly");

            // Assert:
            // - 20261012163335 is removed from __EFMigrationsHistory
            // - 20261011130000 was never in __EFMigrationsHistory
            // - CheckedInAt column is cleanly dropped
            (await MigrationRecordedAsync(context, "20261012163335_AddAppointmentCheckedInAt")).Should().BeFalse();
            (await MigrationRecordedAsync(context, "20261011130000_AddAppointmentCheckIn")).Should().BeFalse();
            (await ColumnExistsAsync(context, "Appointments", "CheckedInAt")).Should().BeFalse(
                "CheckedInAt must be safely dropped when rolling back to before its introduction");
        }
        finally
        {
            SqlServerTestDatabaseSafety.AssertSafeDatabase(context.Database.GetConnectionString()!, dbName);
            await context.Database.EnsureDeletedAsync();
            await context.DisposeAsync();
        }
    }

    [Fact]
    public async Task LegacyDevelopState_TargetingEarlierCheckInMigration_ReconcilesHistoryAndPreservesColumn()
    {
        // Target scenario:
        // Starting from the un-reconciled legacy develop state (20261012163335 recorded, 20261011130000 absent),
        // target 20261011130000 directly.
        var (context, dbName) = CreateIsolatedContext();
        try
        {
            var migrator = context.Database.GetService<IMigrator>();
            await migrator.MigrateAsync("20261009210023_AddAppointmentPaymentDetailsAndBackfill");

            await context.Database.ExecuteSqlRawAsync(
                "IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[Appointments]') AND name = N'CheckedInAt') " +
                "ALTER TABLE [Appointments] ADD [CheckedInAt] datetime2 NULL;");

            await context.Database.ExecuteSqlRawAsync(
                "INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion]) " +
                "VALUES (N'20261012163335_AddAppointmentCheckedInAt', N'8.0.13');");

            // Act: Target 20261011130000_AddAppointmentCheckIn directly
            await migrator.MigrateAsync("20261011130000_AddAppointmentCheckIn");

            // Assert:
            // 20261011130000 is now recorded, 20261012163335 was reverted, and CheckedInAt column is preserved!
            (await MigrationRecordedAsync(context, "20261011130000_AddAppointmentCheckIn")).Should().BeTrue();
            (await MigrationRecordedAsync(context, "20261012163335_AddAppointmentCheckedInAt")).Should().BeFalse();
            (await ColumnExistsAsync(context, "Appointments", "CheckedInAt")).Should().BeTrue();
        }
        finally
        {
            SqlServerTestDatabaseSafety.AssertSafeDatabase(context.Database.GetConnectionString()!, dbName);
            await context.Database.EnsureDeletedAsync();
            await context.DisposeAsync();
        }
    }

    [Fact]
    public async Task FreshDatabase_WhenMigrateAsyncRuns_SucceedsAndAppliesAllMigrations()
    {
        var (context, dbName) = CreateIsolatedContext();
        try
        {
            // Act: Migrate fresh database
            var act = () => context.Database.MigrateAsync();
            await act.Should().NotThrowAsync();

            // Assert: Column exists and both migrations are present
            (await ColumnExistsAsync(context, "Appointments", "CheckedInAt")).Should().BeTrue();
            (await MigrationRecordedAsync(context, "20261011130000_AddAppointmentCheckIn")).Should().BeTrue();
            (await MigrationRecordedAsync(context, "20261012163335_AddAppointmentCheckedInAt")).Should().BeTrue();
        }
        finally
        {
            SqlServerTestDatabaseSafety.AssertSafeDatabase(context.Database.GetConnectionString()!, dbName);
            await context.Database.EnsureDeletedAsync();
            await context.DisposeAsync();
        }
    }

    [Fact]
    public async Task MainBranchState_WhenMigrateAsyncRuns_SucceedsAndAppliesSecondMigrationIdempotently()
    {
        var (context, dbName) = CreateIsolatedContext();
        try
        {
            var migrator = context.Database.GetService<IMigrator>();

            // Setup state as deployed from main (through 20261011130000_AddAppointmentCheckIn)
            await migrator.MigrateAsync("20261011130000_AddAppointmentCheckIn");

            (await ColumnExistsAsync(context, "Appointments", "CheckedInAt")).Should().BeTrue();
            (await MigrationRecordedAsync(context, "20261011130000_AddAppointmentCheckIn")).Should().BeTrue();
            (await MigrationRecordedAsync(context, "20261012163335_AddAppointmentCheckedInAt")).Should().BeFalse();

            // Act: Upgrade to latest reconciled state
            var act = () => context.Database.MigrateAsync();
            await act.Should().NotThrowAsync();

            // Assert: Column preserved and second migration now recorded
            (await ColumnExistsAsync(context, "Appointments", "CheckedInAt")).Should().BeTrue();
            (await MigrationRecordedAsync(context, "20261011130000_AddAppointmentCheckIn")).Should().BeTrue();
            (await MigrationRecordedAsync(context, "20261012163335_AddAppointmentCheckedInAt")).Should().BeTrue();
        }
        finally
        {
            SqlServerTestDatabaseSafety.AssertSafeDatabase(context.Database.GetConnectionString()!, dbName);
            await context.Database.EnsureDeletedAsync();
            await context.DisposeAsync();
        }
    }

    [Fact]
    public async Task MainBranchState_RollbackToBeforeCheckIn_DropsColumnSafely()
    {
        // Target scenario:
        // Database deployed through 20261011130000_AddAppointmentCheckIn (20261012163335 never applied).
        // Rollback to before check-in (20261009210023).
        // 20261011130000.Down() must cleanly drop CheckedInAt because 20261012163335 is not in history.
        var (context, dbName) = CreateIsolatedContext();
        try
        {
            var migrator = context.Database.GetService<IMigrator>();

            await migrator.MigrateAsync("20261011130000_AddAppointmentCheckIn");
            (await ColumnExistsAsync(context, "Appointments", "CheckedInAt")).Should().BeTrue();
            (await MigrationRecordedAsync(context, "20261011130000_AddAppointmentCheckIn")).Should().BeTrue();
            (await MigrationRecordedAsync(context, "20261012163335_AddAppointmentCheckedInAt")).Should().BeFalse();

            // Act: Rollback to immediately before check-in migrations
            await migrator.MigrateAsync("20261009210023_AddAppointmentPaymentDetailsAndBackfill");

            // Assert:
            (await ColumnExistsAsync(context, "Appointments", "CheckedInAt")).Should().BeFalse(
                "CheckedInAt must be safely dropped when rolling back to before its introduction");
            (await MigrationRecordedAsync(context, "20261011130000_AddAppointmentCheckIn")).Should().BeFalse();
            (await MigrationRecordedAsync(context, "20261012163335_AddAppointmentCheckedInAt")).Should().BeFalse();
        }
        finally
        {
            SqlServerTestDatabaseSafety.AssertSafeDatabase(context.Database.GetConnectionString()!, dbName);
            await context.Database.EnsureDeletedAsync();
            await context.DisposeAsync();
        }
    }

    [Fact]
    public async Task Rollback_FromLatestToBeforeCheckInMigrations_DropsColumnSafely()
    {
        var (context, dbName) = CreateIsolatedContext();
        try
        {
            var migrator = context.Database.GetService<IMigrator>();

            // Start fully migrated
            await context.Database.MigrateAsync();
            (await ColumnExistsAsync(context, "Appointments", "CheckedInAt")).Should().BeTrue();

            // Act: Rollback to immediately before check-in migrations
            await migrator.MigrateAsync("20261009210023_AddAppointmentPaymentDetailsAndBackfill");

            // Assert: Column is cleanly dropped and neither check-in migration is recorded
            (await ColumnExistsAsync(context, "Appointments", "CheckedInAt")).Should().BeFalse();
            (await MigrationRecordedAsync(context, "20261011130000_AddAppointmentCheckIn")).Should().BeFalse();
            (await MigrationRecordedAsync(context, "20261012163335_AddAppointmentCheckedInAt")).Should().BeFalse();
        }
        finally
        {
            SqlServerTestDatabaseSafety.AssertSafeDatabase(context.Database.GetConnectionString()!, dbName);
            await context.Database.EnsureDeletedAsync();
            await context.DisposeAsync();
        }
    }
}
