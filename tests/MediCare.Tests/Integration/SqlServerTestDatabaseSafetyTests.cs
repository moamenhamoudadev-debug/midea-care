using FluentAssertions;
using Xunit;

namespace MediCare.Tests.Integration;

public sealed class SqlServerTestDatabaseSafetyTests
{
    [Fact]
    public void ForGeneratedDatabase_RejectsNonTestDatabaseName()
    {
        var act = () => SqlServerTestDatabaseSafety.ForGeneratedDatabase(
            "Server=127.0.0.1,1433;Database=MediCare_IntegrationTests_0123456789abcdef0123456789abcdef;",
            "Production");

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ForGeneratedDatabase_RejectsUnapprovedServer()
    {
        var act = () => SqlServerTestDatabaseSafety.ForGeneratedDatabase(
            "Server=sql.example.com;Database=MediCare_IntegrationTests_0123456789abcdef0123456789abcdef;",
            "MediCare_MigTest_0123456789abcdef0123456789abcdef");

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void AssertSafeDatabase_RejectsDatabaseNameMismatch()
    {
        var act = () => SqlServerTestDatabaseSafety.AssertSafeDatabase(
            "Server=127.0.0.1,1433;Database=MediCare_MigTest_0123456789abcdef0123456789abcdef;",
            "MediCare_IntegrationTests_0123456789abcdef0123456789abcdef");

        act.Should().Throw<InvalidOperationException>();
    }
}
