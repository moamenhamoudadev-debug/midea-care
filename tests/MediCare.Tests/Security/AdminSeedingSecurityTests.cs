using FluentAssertions;
using MediCare.Data.Context;
using MediCare.Data.Entities;
using MediCare.Data.Seed;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using Xunit;

namespace MediCare.Tests.Security;

public class AdminSeedingSecurityTests
{
    private ServiceProvider BuildSeedServiceProvider(string environmentName, Dictionary<string, string?> configValues)
    {
        var services = new ServiceCollection();

        var dbName = Guid.NewGuid().ToString();
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase(dbName));

        services.AddIdentity<ApplicationUser, IdentityRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddDefaultTokenProviders();

        services.AddLogging();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configValues)
            .Build();
        services.AddSingleton<IConfiguration>(configuration);

        var hostEnvMock = new Mock<IHostEnvironment>();
        hostEnvMock.Setup(h => h.EnvironmentName).Returns(environmentName);
        services.AddSingleton(hostEnvMock.Object);

        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task DbInitializer_InProduction_WhenAdminPasswordMissing_ThrowsCriticalException()
    {
        // Arrange: Production environment without Seed:AdminPassword
        var config = new Dictionary<string, string?>
        {
            ["Seed:OtherKey"] = "val",
            ["Seed:DefaultPassword"] = "SecureDevelopmentSeed!2026"
        };
        using var sp = BuildSeedServiceProvider("Production", config);

        // Act: Must fail startup
        var act = async () => await DbInitializer.InitializeAsync(sp);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Critical Security Failure*");
    }

    [Fact]
    public async Task DbInitializer_InProduction_WhenAdminPasswordConfigured_SeedsAdminSuccessfully()
    {
        // Arrange: Production environment with a strong secret
        var config = new Dictionary<string, string?>
        {
            ["Seed:AdminPassword"] = "SuperSecureAdm!n2026"
        };
        using var sp = BuildSeedServiceProvider("Production", config);

        // Act
        await DbInitializer.InitializeAsync(sp);

        // Assert
        using var scope = sp.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var admin = await userManager.FindByEmailAsync("admin@medicare.com");
        admin.Should().NotBeNull();
        admin!.Email.Should().Be("admin@medicare.com");

        var roles = await userManager.GetRolesAsync(admin);
        roles.Should().Contain("Admin");
    }

    [Fact]
    public async Task DbInitializer_InDevelopment_WhenAdminPasswordMissing_DoesNotThrowAndSeedsAdmin()
    {
        // Arrange: Development mode can fall back to dev password
        var config = new Dictionary<string, string?>
        {
            ["Seed:OtherKey"] = "val",
            ["Seed:DefaultPassword"] = "SecureDevelopmentSeed!2026"
        };
        using var sp = BuildSeedServiceProvider("Development", config);

        // Act
        await DbInitializer.InitializeAsync(sp);

        // Assert
        using var scope = sp.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var admin = await userManager.FindByEmailAsync("admin@medicare.com");
        admin.Should().NotBeNull();
    }

    [Fact]
    public async Task DbInitializer_InProduction_WhenAdminPasswordShorterThan16Chars_ThrowsCriticalException()
    {
        // Arrange: Production environment with password shorter than 16 characters
        var config = new Dictionary<string, string?>
        {
            ["Seed:AdminPassword"] = "ShortPass123!" // 13 chars (< 16)
        };
        using var sp = BuildSeedServiceProvider("Production", config);

        // Act
        var act = async () => await DbInitializer.InitializeAsync(sp);

        // Assert: Must enforce at least 16 characters
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*16 characters*");
    }

    [Fact]
    public async Task DbInitializer_AdminAccount_HasLockoutEnabledTrue()
    {
        // Arrange
        var config = new Dictionary<string, string?>
        {
            ["Seed:AdminPassword"] = "SuperSecureAdm!n2026_Length16"
        };
        using var sp = BuildSeedServiceProvider("Production", config);

        // Act
        await DbInitializer.InitializeAsync(sp);

        // Assert: Admin must have lockout enabled for brute-force defense
        using var scope = sp.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var admin = await userManager.FindByEmailAsync("admin@medicare.com");
        admin.Should().NotBeNull();
        admin!.LockoutEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task DbInitializer_WhenExistingAdminHasLockoutDisabled_EnablesLockout()
    {
        // Arrange: Pre-populate database with an admin user whose LockoutEnabled is false
        var config = new Dictionary<string, string?>
        {
            ["Seed:AdminPassword"] = "SuperSecureAdm!n2026_Length16"
        };
        using var sp = BuildSeedServiceProvider("Production", config);

        using (var scope = sp.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var preAdmin = new ApplicationUser
            {
                UserName = "admin@medicare.com",
                Email = "admin@medicare.com",
                FullName = "System Administrator",
                PhoneNumber = "+201000000001",
                EmailConfirmed = true,
                LockoutEnabled = false,
                AccessFailedCount = 2
            };
            await userManager.CreateAsync(preAdmin, "SuperSecureAdm!n2026_Length16");
        }

        // Act: Initialize should remediate existing admin and ensure LockoutEnabled is true
        await DbInitializer.InitializeAsync(sp);

        // Assert
        using (var scope = sp.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var admin = await userManager.FindByEmailAsync("admin@medicare.com");
            admin.Should().NotBeNull();
            admin!.LockoutEnabled.Should().BeTrue();
        }
    }

    [Fact]
    public async Task DbInitializer_InDevelopment_PreservesFailedAttemptsAndLockoutAcrossStartup()
    {
        // Arrange: In Development, admin was locked out during local test run
        var config = new Dictionary<string, string?>
        {
            ["Seed:DefaultPassword"] = "Demo@Pass123!"
        };
        using var sp = BuildSeedServiceProvider("Development", config);

        using (var scope = sp.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var preAdmin = new ApplicationUser
            {
                UserName = "admin@medicare.com",
                Email = "admin@medicare.com",
                FullName = "System Administrator",
                PhoneNumber = "+201000000001",
                EmailConfirmed = true,
                LockoutEnabled = true,
                AccessFailedCount = 5,
                LockoutEnd = DateTimeOffset.UtcNow.AddMinutes(15)
            };
            await userManager.CreateAsync(preAdmin, "Demo@Pass123!");
        }

        // Act: Re-initializing must not bypass the account lockout.
        await DbInitializer.InitializeAsync(sp);

        // Assert
        using (var scope = sp.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var admin = await userManager.FindByEmailAsync("admin@medicare.com");
            admin.Should().NotBeNull();
            admin!.LockoutEnabled.Should().BeTrue();
            admin.LockoutEnd.Should().NotBeNull();
            admin.LockoutEnd.Should().BeAfter(DateTimeOffset.UtcNow);
            admin.AccessFailedCount.Should().Be(5);
        }
    }

    [Fact]
    public async Task DbInitializer_InProduction_NeverSeedsDemoDoctorsOrPatients()
    {
        // Arrange: Production environment with a strong secret
        var config = new Dictionary<string, string?>
        {
            ["Seed:AdminPassword"] = "SuperSecureAdm!n2026_Length16"
        };
        using var sp = BuildSeedServiceProvider("Production", config);

        // Act
        await DbInitializer.InitializeAsync(sp);

        // Assert: Production must only have Admin and Specializations, zero demo doctors or patients
        using var scope = sp.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var doctorsCount = await context.Doctors.CountAsync();
        var patientsCount = await context.Patients.CountAsync();
        var appointmentsCount = await context.Appointments.CountAsync();

        doctorsCount.Should().Be(0, "Demo doctors must never be seeded in Production");
        patientsCount.Should().Be(0, "Demo patients must never be seeded in Production");
        appointmentsCount.Should().Be(0, "Demo appointments must never be seeded in Production");

        var users = await userManager.Users.ToListAsync();
        users.Should().ContainSingle(u => u.Email == "admin@medicare.com");
    }

    [Fact]
    public async Task DbInitializer_RejectsInvalidApplyMigrationsOnStartupValue()
    {
        var config = new Dictionary<string, string?>
        {
            ["Seed:AdminPassword"] = "SuperSecureAdm!n2026_Length16",
            ["Database:ApplyMigrationsOnStartup"] = "sometimes"
        };
        using var sp = BuildSeedServiceProvider("Production", config);

        var act = () => DbInitializer.InitializeAsync(sp);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*ApplyMigrationsOnStartup must be set to 'true' or 'false'*");
    }
}
