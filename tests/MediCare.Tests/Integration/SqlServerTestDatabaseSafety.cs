using Microsoft.Data.SqlClient;

namespace MediCare.Tests.Integration;

internal static class SqlServerTestDatabaseSafety
{
    private static readonly string[] AllowedServers =
    [
        @"(localdb)\mssqllocaldb",
        "127.0.0.1,1433"
    ];

    public static string ReadBaseConnectionString()
    {
        var value = Environment.GetEnvironmentVariable("MEDICARE_TEST_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                "SQL Server integration tests require MEDICARE_TEST_CONNECTION_STRING pointing to an isolated test SQL Server and database.");
        }

        var builder = new SqlConnectionStringBuilder(value);
        ValidateServer(builder);

        if (!IsValidRunDatabaseName(builder.InitialCatalog))
        {
            throw new InvalidOperationException(
                "The configured SQL test database must be named MediCare_IntegrationTests_<32-hex-run-id> or MediCare_IntegrationTests_CI_<run-id>_<attempt>.");
        }

        return builder.ConnectionString;
    }

    public static string ForGeneratedDatabase(string baseConnectionString, string databaseName)
    {
        var builder = new SqlConnectionStringBuilder(baseConnectionString);
        ValidateServer(builder);

        if (!IsGeneratedDatabaseName(databaseName))
        {
            throw new InvalidOperationException(
                $"Refusing to use SQL database '{databaseName}'. Generated integration databases must use a supported MediCare test prefix and GUID.");
        }

        builder.InitialCatalog = databaseName;
        return builder.ConnectionString;
    }

    public static void AssertSafeDatabase(string connectionString, string expectedDatabaseName)
    {
        var builder = new SqlConnectionStringBuilder(connectionString);
        ValidateServer(builder);

        if (!string.Equals(builder.InitialCatalog, expectedDatabaseName, StringComparison.OrdinalIgnoreCase) ||
            !(IsValidRunDatabaseName(builder.InitialCatalog) || IsGeneratedDatabaseName(builder.InitialCatalog)))
        {
            throw new InvalidOperationException(
                $"Refusing destructive SQL test cleanup for database '{builder.InitialCatalog}'; it does not match the verified isolated test database name.");
        }
    }

    private static void ValidateServer(SqlConnectionStringBuilder builder)
    {
        if (!AllowedServers.Contains(builder.DataSource, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "SQL Server integration tests may connect only to the current user's MSSQLLocalDB instance or the CI SQL Server service at 127.0.0.1,1433.");
        }

        if (builder.AttachDBFilename.Length > 0 || builder.UserInstance)
        {
            throw new InvalidOperationException("SQL Server integration tests do not allow attached database files or user instances.");
        }
    }

    private static bool IsValidRunDatabaseName(string name) =>
        System.Text.RegularExpressions.Regex.IsMatch(
            name,
            @"^MediCare_IntegrationTests_(?:[0-9a-f]{32}|CI_[0-9]+_[0-9]+)$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    private static bool IsGeneratedDatabaseName(string name) =>
        System.Text.RegularExpressions.Regex.IsMatch(
            name,
            @"^MediCare_(?:MigTest|Test|Perf)_[0-9a-f]{32}$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
}
