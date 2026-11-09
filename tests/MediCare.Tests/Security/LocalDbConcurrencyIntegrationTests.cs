using System.Runtime.InteropServices;
using FluentAssertions;
using MediCare.Data.Context;
using MediCare.Data.Entities;
using MediCare.Data.Enums;
using MediCare.Data.Repositories;
using MediCare.Tests.Integration;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MediCare.Tests.Security;

[Trait("Category", "Integration")]
[Collection("SqlServerIntegration")]
public class LocalDbConcurrencyIntegrationTests
{
    private readonly SqlServerDatabaseFixture? _fixture;

    public LocalDbConcurrencyIntegrationTests(SqlServerDatabaseFixture? fixture = null)
    {
        _fixture = fixture;
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Prescription_SqlServerLocalDb_ThrowsDbUpdateConcurrencyException_OnConcurrentDispense()
    {
        var dbName = $"MediCare_Test_{Guid.NewGuid():N}";
        var connectionString = GetTestConnectionString(dbName);

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        using var setupContext = new ApplicationDbContext(options);

        try
        {
            // Create database schema on target SQL Server instance
            var created = await setupContext.Database.EnsureCreatedAsync();
            created.Should().BeTrue("a fresh isolated test database should be created for concurrency verification");

            // Seed relational prerequisites (Users, Doctor, Patient, Appointment, MedicalRecord) with unique identifiers
            var uniqueSuffix = Guid.NewGuid().ToString("N")[..8];
            var docUser = new ApplicationUser
            {
                Id = Guid.NewGuid().ToString(),
                UserName = $"doc_{uniqueSuffix}@test.com",
                NormalizedUserName = $"DOC_{uniqueSuffix.ToUpperInvariant()}@TEST.COM",
                Email = $"doc_{uniqueSuffix}@test.com",
                NormalizedEmail = $"DOC_{uniqueSuffix.ToUpperInvariant()}@TEST.COM",
                FullName = "Dr. Test",
                SecurityStamp = Guid.NewGuid().ToString()
            };
            var patUser = new ApplicationUser
            {
                Id = Guid.NewGuid().ToString(),
                UserName = $"pat_{uniqueSuffix}@test.com",
                NormalizedUserName = $"PAT_{uniqueSuffix.ToUpperInvariant()}@TEST.COM",
                Email = $"pat_{uniqueSuffix}@test.com",
                NormalizedEmail = $"PAT_{uniqueSuffix.ToUpperInvariant()}@TEST.COM",
                FullName = "Patient Test",
                SecurityStamp = Guid.NewGuid().ToString()
            };
            setupContext.Users.AddRange(docUser, patUser);

            var spec = new Specialization { Name = $"Cardiology_{uniqueSuffix}" };
            setupContext.Specializations.Add(spec);

            var doctor = new Doctor
            {
                UserId = docUser.Id,
                Specialization = spec,
                ConsultationFee = 100,
                LicenseNumber = $"DOC-{uniqueSuffix}"
            };
            setupContext.Doctors.Add(doctor);

            var patient = new Patient
            {
                UserId = patUser.Id,
                Gender = "Male",
                DateOfBirth = new DateTime(1990, 1, 1)
            };
            setupContext.Patients.Add(patient);

            var appointment = new Appointment
            {
                Doctor = doctor,
                Patient = patient,
                AppointmentDate = DateTime.Today,
                StartTime = new TimeSpan(10, 0, 0),
                EndTime = new TimeSpan(10, 30, 0),
                Status = AppointmentStatus.Completed,
                ConsultationFee = 100,
                PaymentStatus = PaymentStatus.Paid
            };
            setupContext.Appointments.Add(appointment);

            var record = new MedicalRecord
            {
                Doctor = doctor,
                Patient = patient,
                Appointment = appointment,
                Diagnosis = "Healthy",
                VisitNotes = "Follow-up"
            };
            setupContext.MedicalRecords.Add(record);

            var prescription = new Prescription
            {
                Doctor = doctor,
                Patient = patient,
                MedicalRecord = record,
                VerificationToken = "testtoken" + Guid.NewGuid().ToString("N")[..23],
                PrescriptionDate = DateTime.Today,
                IsDispensed = false,
                Notes = "Take once daily"
            };
            setupContext.Prescriptions.Add(prescription);
            await setupContext.SaveChangesAsync();
            var prescriptionId = prescription.Id;

            // Two distinct DbContext instances simulating two simultaneous requests across threads
            using var context1 = new ApplicationDbContext(options);
            using var context2 = new ApplicationDbContext(options);

            var presc1 = await context1.Prescriptions.FindAsync(prescriptionId);
            var presc2 = await context2.Prescriptions.FindAsync(prescriptionId);

            presc1.Should().NotBeNull();
            presc2.Should().NotBeNull();

            // Both prepare to dispense
            presc1!.IsDispensed = true;
            presc1.DispensedAt = DateTime.UtcNow;

            presc2!.IsDispensed = true;
            presc2.DispensedAt = DateTime.UtcNow;

            // First request commits successfully to SQL Server
            var rows1 = await context1.SaveChangesAsync();
            rows1.Should().BeGreaterThan(0);

            // Second request attempts commit with stale concurrency token
            // SQL Server executes: UPDATE [Prescriptions] SET [IsDispensed] = 1 WHERE [Id] = @p0 AND [IsDispensed] = 0
            var act = async () => await context2.SaveChangesAsync();

            // Assert: Real SQL Server evaluates 0 rows affected and EF Core raises DbUpdateConcurrencyException
            await act.Should().ThrowAsync<DbUpdateConcurrencyException>();
        }
        finally
        {
            SqlServerTestDatabaseSafety.AssertSafeDatabase(connectionString, dbName);
            try
            {
                SqlConnection.ClearAllPools();
                await setupContext.Database.EnsureDeletedAsync();
            }
            catch
            {
                // Suppress cleanup failure so primary test outcome is preserved
            }
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task DashboardMetrics_SqlServerLocalDb_TranslatesAggregationsAndExecutesOnEngine()
    {
        var dbName = $"MediCare_Perf_{Guid.NewGuid():N}";
        var connectionString = GetTestConnectionString(dbName);

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        using var setupContext = new ApplicationDbContext(options);

        try
        {
            var created = await setupContext.Database.EnsureCreatedAsync();
            created.Should().BeTrue("a fresh isolated test database should be created for dashboard SQL aggregation verification");

            var spec = new Specialization { Name = "Cardiology" };
            setupContext.Specializations.Add(spec);
            await setupContext.SaveChangesAsync();

            var uniqueSuffix = Guid.NewGuid().ToString("N")[..8];
            var docUser = new ApplicationUser
            {
                Id = Guid.NewGuid().ToString(),
                UserName = $"doc_{uniqueSuffix}@test.com",
                NormalizedUserName = $"DOC_{uniqueSuffix.ToUpperInvariant()}@TEST.COM",
                Email = $"doc_{uniqueSuffix}@test.com",
                NormalizedEmail = $"DOC_{uniqueSuffix.ToUpperInvariant()}@TEST.COM",
                FullName = "Dr. Server Perf",
                SecurityStamp = Guid.NewGuid().ToString()
            };
            var patUser = new ApplicationUser
            {
                Id = Guid.NewGuid().ToString(),
                UserName = $"pat_{uniqueSuffix}@test.com",
                NormalizedUserName = $"PAT_{uniqueSuffix.ToUpperInvariant()}@TEST.COM",
                Email = $"pat_{uniqueSuffix}@test.com",
                NormalizedEmail = $"PAT_{uniqueSuffix.ToUpperInvariant()}@TEST.COM",
                FullName = "Patient Server Perf",
                SecurityStamp = Guid.NewGuid().ToString()
            };
            setupContext.Users.AddRange(docUser, patUser);
            await setupContext.SaveChangesAsync();

            var doctor = new Doctor
            {
                UserId = docUser.Id,
                SpecializationId = spec.Id,
                LicenseNumber = $"LIC-{uniqueSuffix}",
                ConsultationFee = 500m,
                IsApproved = true
            };
            var patient = new Patient
            {
                UserId = patUser.Id,
                DateOfBirth = new DateTime(1995, 6, 15),
                Gender = "Male"
            };
            setupContext.Doctors.Add(doctor);
            setupContext.Patients.Add(patient);
            await setupContext.SaveChangesAsync();

            var clinicToday = new DateTime(2026, 10, 15);
            var appt1 = new Appointment
            {
                DoctorId = doctor.Id,
                PatientId = patient.Id,
                AppointmentDate = clinicToday,
                StartTime = new TimeSpan(10, 0, 0),
                EndTime = new TimeSpan(10, 30, 0),
                Status = AppointmentStatus.Completed,
                PaymentStatus = PaymentStatus.Paid,
                ConsultationFee = 500m,
                DiscountAmount = 50m,
                PaidAmount = 450m
            };

            var appt2 = new Appointment
            {
                DoctorId = doctor.Id,
                PatientId = patient.Id,
                AppointmentDate = clinicToday,
                StartTime = new TimeSpan(11, 0, 0),
                EndTime = new TimeSpan(11, 30, 0),
                Status = AppointmentStatus.Completed,
                PaymentStatus = PaymentStatus.Paid,
                ConsultationFee = 350m,
                DiscountAmount = 100m,
                PaidAmount = null
            };

            var appt3 = new Appointment
            {
                DoctorId = doctor.Id,
                PatientId = patient.Id,
                AppointmentDate = clinicToday,
                StartTime = new TimeSpan(12, 0, 0),
                EndTime = new TimeSpan(12, 30, 0),
                Status = AppointmentStatus.Completed,
                PaymentStatus = PaymentStatus.Paid,
                ConsultationFee = 200m,
                DiscountAmount = null,
                PaidAmount = null
            };

            var appt4 = new Appointment
            {
                DoctorId = doctor.Id,
                PatientId = patient.Id,
                AppointmentDate = clinicToday,
                StartTime = new TimeSpan(13, 0, 0),
                EndTime = new TimeSpan(13, 30, 0),
                Status = AppointmentStatus.Cancelled,
                PaymentStatus = PaymentStatus.Refunded,
                ConsultationFee = 300m,
                DiscountAmount = null,
                PaidAmount = null
            };

            setupContext.Appointments.AddRange(appt1, appt2, appt3, appt4);
            await setupContext.SaveChangesAsync();

            // Act: Execute GetDashboardMetricsSummaryAsync using SQL Server provider
            var repo = new AppointmentRepository(setupContext);
            var summary = await repo.GetDashboardMetricsSummaryAsync(clinicToday);

            // Assert: Server-side SQL aggregations executed successfully on genuine SQL Server
            summary.Should().NotBeNull();
            summary!.TotalAppointments.Should().Be(4);
            summary.CompletedVisitsCount.Should().Be(3);
            summary.ActiveDoctorsCount.Should().Be(1);
            summary.PendingDoctorsCount.Should().Be(0);
            // 450 + 250 + 200 = 900
            summary.TotalRevenueCollected.Should().Be(900m);
            // Refunded: 300
            summary.TotalRefundedRevenue.Should().Be(300m);
            summary.RefundedAppointmentsCount.Should().Be(1);
            summary.Demographics.TotalPatients.Should().Be(1);
            summary.Demographics.MaleCount.Should().Be(1);
            summary.Demographics.Age18To35Count.Should().Be(1);
            summary.MonthlyTrends.Should().HaveCount(12);
            summary.Specializations.Should().HaveCount(1);
            summary.Specializations[0].SpecializationName.Should().Be("Cardiology");
            summary.Specializations[0].AppointmentCount.Should().Be(4);
            summary.TopDoctors.Should().HaveCount(1);
            summary.TopDoctors[0].DoctorName.Should().Be("Dr. Server Perf");
            summary.TopDoctors[0].TotalRevenue.Should().Be(900m);
        }
        finally
        {
            SqlServerTestDatabaseSafety.AssertSafeDatabase(connectionString, dbName);
            try
            {
                SqlConnection.ClearAllPools();
                await setupContext.Database.EnsureDeletedAsync();
            }
            catch
            {
                // Suppress cleanup failure so primary test outcome is preserved
            }
        }
    }

    private string GetTestConnectionString(string databaseName)

    {
        var rawConn = _fixture?.ConnectionString ?? SqlServerTestDatabaseSafety.ReadBaseConnectionString();
        return SqlServerTestDatabaseSafety.ForGeneratedDatabase(rawConn, databaseName);
    }
}
