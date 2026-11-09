using FluentAssertions;
using MediCare.Data.Context;
using MediCare.Data.Entities;
using MediCare.Data.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MediCare.Tests.Repositories;

public class DoctorRepositoryIntegrationTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly DoctorRepository _repository;

    public DoctorRepositoryIntegrationTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new ApplicationDbContext(options);
        _repository = new DoctorRepository(_context);

        SeedDatabase();
    }

    private void SeedDatabase()
    {
        var specCardio = new Specialization { Id = 1, Name = "Cardiology" };
        var specDerma = new Specialization { Id = 2, Name = "Dermatology" };
        _context.Specializations.AddRange(specCardio, specDerma);

        var user1 = new ApplicationUser { Id = "u1", FullName = "Dr. Ahmed Mahmoud", Email = "ahmed@clinic.com" };
        var user2 = new ApplicationUser { Id = "u2", FullName = "Dr. Sara Al-Sayed", Email = "sara@clinic.com" };
        var user3 = new ApplicationUser { Id = "u3", FullName = "Dr. Unapproved Doctor", Email = "unapproved@clinic.com" };
        _context.Users.AddRange(user1, user2, user3);

        var doc1 = new Doctor
        {
            Id = 1,
            UserId = "u1",
            SpecializationId = 1,
            LicenseNumber = "LIC-001",
            ConsultationFee = 300m,
            Governorate = "Cairo",
            IsApproved = true,
            WorkingHours = new List<WorkingHours>
            {
                new() { DayOfWeek = DayOfWeek.Sunday, StartTime = new TimeSpan(9, 0, 0), EndTime = new TimeSpan(17, 0, 0) },
                new() { DayOfWeek = DayOfWeek.Tuesday, StartTime = new TimeSpan(9, 0, 0), EndTime = new TimeSpan(17, 0, 0) }
            }
        };

        var doc2 = new Doctor
        {
            Id = 2,
            UserId = "u2",
            SpecializationId = 2,
            LicenseNumber = "LIC-002",
            ConsultationFee = 250m,
            Governorate = "Alexandria",
            IsApproved = true,
            WorkingHours = new List<WorkingHours>
            {
                new() { DayOfWeek = DayOfWeek.Monday, StartTime = new TimeSpan(10, 0, 0), EndTime = new TimeSpan(18, 0, 0) }
            }
        };

        var doc3 = new Doctor
        {
            Id = 3,
            UserId = "u3",
            SpecializationId = 1,
            LicenseNumber = "LIC-003",
            ConsultationFee = 200m,
            IsApproved = false // Pending approval
        };

        _context.Doctors.AddRange(doc1, doc2, doc3);
        _context.SaveChanges();
    }

    [Fact]
    public async Task SearchApprovedDoctorsAsync_ExcludesUnapprovedDoctors()
    {
        // Act
        var (doctors, totalCount) = await _repository.SearchApprovedDoctorsAsync(
            null, null, null, null, 1, 10);

        // Assert
        totalCount.Should().Be(2);
        doctors.Should().HaveCount(2);
        doctors.Should().NotContain(d => d.IsApproved == false);
    }

    [Fact]
    public async Task SearchApprovedDoctorsAsync_FiltersBySpecialization()
    {
        // Act
        var (doctors, totalCount) = await _repository.SearchApprovedDoctorsAsync(
            1, null, null, null, 1, 10);

        // Assert
        totalCount.Should().Be(1);
        doctors.Single().User.FullName.Should().Be("Dr. Ahmed Mahmoud");
    }

    [Fact]
    public async Task SearchApprovedDoctorsAsync_FiltersByMaxFee()
    {
        // Act
        var (doctors, totalCount) = await _repository.SearchApprovedDoctorsAsync(
            null, 280m, null, null, 1, 10);

        // Assert
        totalCount.Should().Be(1);
        doctors.Single().ConsultationFee.Should().Be(250m);
    }

    [Fact]
    public async Task SearchApprovedDoctorsAsync_FiltersByAvailableDay()
    {
        // Act
        var (doctors, totalCount) = await _repository.SearchApprovedDoctorsAsync(
            null, null, DayOfWeek.Sunday, null, 1, 10);

        // Assert
        totalCount.Should().Be(1);
        doctors.Single().User.FullName.Should().Be("Dr. Ahmed Mahmoud");
    }

    [Fact]
    public async Task SearchApprovedDoctorsAsync_FiltersByGovernorate()
    {
        // Act
        var (doctors, totalCount) = await _repository.SearchApprovedDoctorsAsync(
            null, null, null, null, "Alexandria", 1, 10);

        // Assert
        totalCount.Should().Be(1);
        doctors.Single().User.FullName.Should().Be("Dr. Sara Al-Sayed");
    }

    /*
     * ARCHITECTURAL NOTE ON PROVIDER LIMITATIONS:
     * In-memory database providers (such as Microsoft.EntityFrameworkCore.InMemory) do not simulate
     * SQL Server relational engine constraints, specifically:
     * 1. Filtered Unique Indexes: [IX_Appointments_Doctor_NoOverlap] WHERE [Status] <> 3 AND [Status] <> 4
     * 2. FOREIGN KEY cascade/restrict constraints.
     * The concurrency-safe filtered unique index and relational constraints were validated against
     * the SQL Server LocalDB instance during EF Core migration execution (InitialCreate).
     */

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }
}
