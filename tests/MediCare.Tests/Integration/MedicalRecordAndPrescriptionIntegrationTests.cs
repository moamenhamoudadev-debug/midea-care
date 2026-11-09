using FluentAssertions;
using MediCare.Data.Context;
using MediCare.Data.Entities;
using MediCare.Data.Enums;
using MediCare.Data.UnitOfWork;
using MediCare.Services.Common;
using MediCare.Services.Contracts;
using MediCare.Services.DTOs;
using MediCare.Services.Implementations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace MediCare.Tests.Integration;

[Trait("Category", "Integration")]
[Collection("SqlServerIntegration")]
public class MedicalRecordAndPrescriptionIntegrationTests : IAsyncLifetime
{
    private readonly DbContextOptions<ApplicationDbContext> _options;
    private int _doctorId;
    private int _patientId;
    private string _doctorUserId = null!;
    private string _patientUserId = null!;

    public MedicalRecordAndPrescriptionIntegrationTests(SqlServerDatabaseFixture fixture)
    {
        _options = fixture.Options;
    }

    public async Task InitializeAsync()
    {
        using var context = new ApplicationDbContext(_options);

        // Ensure baseline specialization exists
        var spec = await context.Specializations.FirstOrDefaultAsync();
        if (spec == null)
        {
            spec = new Specialization { Name = "Cardiology", Description = "Cardiology clinic" };
            context.Specializations.Add(spec);
            await context.SaveChangesAsync();
        }

        // Create test doctor and patient
        _doctorUserId = Guid.NewGuid().ToString("N");
        _patientUserId = Guid.NewGuid().ToString("N");

        var docUser = new ApplicationUser
        {
            Id = _doctorUserId,
            UserName = $"doc_mr_{_doctorUserId[..6]}@medicare.test",
            Email = $"doc_mr_{_doctorUserId[..6]}@medicare.test",
            FullName = "Dr. Integration Test"
        };

        var patUser = new ApplicationUser
        {
            Id = _patientUserId,
            UserName = $"pat_mr_{_patientUserId[..6]}@medicare.test",
            Email = $"pat_mr_{_patientUserId[..6]}@medicare.test",
            FullName = "Patient Integration Test"
        };

        context.Users.AddRange(docUser, patUser);
        await context.SaveChangesAsync();

        var doctor = new Doctor
        {
            UserId = docUser.Id,
            SpecializationId = spec.Id,
            LicenseNumber = $"LIC-MR-{Guid.NewGuid().ToString("N")[..6]}",
            ConsultationFee = 400,
            SlotDurationMinutes = 30,
            IsApproved = true
        };

        var patient = new Patient
        {
            UserId = patUser.Id,
            DateOfBirth = new DateTime(1995, 5, 20),
            Gender = "Male",
            BloodGroup = "O+"
        };

        context.Doctors.Add(doctor);
        context.Patients.Add(patient);
        await context.SaveChangesAsync();

        _doctorId = doctor.Id;
        _patientId = patient.Id;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task SaveEncounter_RealSqlServer_AtomicallyCompletesAppointmentAndInsertsRecordWithPrescriptionAndItems()
    {
        // 1. Arrange: Insert a Confirmed appointment in SQL Server whose scheduled time has elapsed
        int appointmentId;
        using (var setupContext = new ApplicationDbContext(_options))
        {
            var appt = new Appointment
            {
                DoctorId = _doctorId,
                PatientId = _patientId,
                AppointmentDate = new DateTime(2026, 1, 10),
                StartTime = new TimeSpan(9, 0, 0),
                EndTime = new TimeSpan(9, 30, 0),
                Status = AppointmentStatus.Confirmed,
                PaymentStatus = PaymentStatus.Unpaid,
                ConsultationFee = 400,
                Type = AppointmentType.Consultation
            };
            setupContext.Appointments.Add(appt);
            await setupContext.SaveChangesAsync();
            appointmentId = appt.Id;
        }

        // Mock clinic clock past the appointment time
        var clockMock = new Mock<IClinicClock>();
        clockMock.Setup(c => c.Now).Returns(new DateTime(2026, 1, 10, 10, 0, 0));
        clockMock.Setup(c => c.Today).Returns(new DateTime(2026, 1, 10));

        var notifMock = new Mock<INotificationService>();
        var fileMock = new Mock<IFileStorageService>();

        // 2. Act: Call MedicalRecordService against real SQL Server UnitOfWork
        using (var actContext = new ApplicationDbContext(_options))
        {
            var uow = new UnitOfWork(actContext);
            var service = new MedicalRecordService(
                uow,
                fileMock.Object,
                clockMock.Object,
                notifMock.Object,
                NullLogger<MedicalRecordService>.Instance);

            var encounterDto = new CreateEncounterDto
            {
                AppointmentId = appointmentId,
                Diagnosis = "Hypertension and Mild Bronchitis",
                Symptoms = "Chest tightness, blood pressure 145/95",
                VisitNotes = "Monitor BP daily and avoid caffeine",
                Notes = "Take medications on schedule",
                PrescriptionItems = new List<PrescriptionItemDto>
                {
                    new PrescriptionItemDto
                    {
                        MedicationName = "Concor 5mg",
                        Dosage = "1 tablet",
                        Frequency = "Once daily in morning",
                        DurationDays = 30,
                        Instructions = "Before breakfast"
                    },
                    new PrescriptionItemDto
                    {
                        MedicationName = "Ventolin Inhaler 100mcg",
                        Dosage = "2 puffs",
                        Frequency = "As needed",
                        DurationDays = 14,
                        Instructions = "When feeling short of breath"
                    }
                }
            };

            var result = await service.SaveEncounterAsync(encounterDto, null, _doctorUserId, "C:\\test\\webroot");
            result.IsSuccess.Should().BeTrue();
            result.Value.Should().BeGreaterThan(0);
        }

        // 3. Assert: Verify in a completely separate DbContext instance
        using (var verifyContext = new ApplicationDbContext(_options))
        {
            var updatedAppt = await verifyContext.Appointments.FindAsync(appointmentId);
            updatedAppt.Should().NotBeNull();
            updatedAppt!.Status.Should().Be(AppointmentStatus.Completed);
            updatedAppt.PaymentStatus.Should().Be(PaymentStatus.Paid);

            var record = await verifyContext.MedicalRecords
                .Include(r => r.Prescription)
                .ThenInclude(p => p!.Items)
                .FirstOrDefaultAsync(r => r.AppointmentId == appointmentId);

            record.Should().NotBeNull();
            record!.Diagnosis.Should().Be("Hypertension and Mild Bronchitis");
            record.DoctorId.Should().Be(_doctorId);
            record.PatientId.Should().Be(_patientId);

            record.Prescription.Should().NotBeNull();
            record.Prescription!.DoctorId.Should().Be(_doctorId);
            record.Prescription.PatientId.Should().Be(_patientId);
            record.Prescription.Items.Should().HaveCount(2);

            var items = record.Prescription.Items.OrderBy(i => i.MedicationName).ToList();
            items[0].MedicationName.Should().Be("Concor 5mg");
            items[0].DurationDays.Should().Be(30);
            items[1].MedicationName.Should().Be("Ventolin Inhaler 100mcg");
            items[1].DurationDays.Should().Be(14);
        }
    }

    [Fact]
    public async Task AdminApproveDoctor_RealSqlServer_PersistsApprovalStatus()
    {
        // 1. Arrange: Create unapproved doctor in SQL Server
        int pendingDocId;
        using (var setupContext = new ApplicationDbContext(_options))
        {
            var unapprovedUser = new ApplicationUser
            {
                Id = Guid.NewGuid().ToString("N"),
                UserName = $"unapp_{Guid.NewGuid().ToString("N")[..6]}@test.com",
                Email = "pending.doc@test.com",
                FullName = "Dr. Unapproved Integrator"
            };
            setupContext.Users.Add(unapprovedUser);
            await setupContext.SaveChangesAsync();

            var spec = await setupContext.Specializations.FirstAsync();
            var doc = new Doctor
            {
                UserId = unapprovedUser.Id,
                SpecializationId = spec.Id,
                LicenseNumber = $"PEND-{Guid.NewGuid().ToString("N")[..6]}",
                ConsultationFee = 250,
                SlotDurationMinutes = 20,
                IsApproved = false
            };
            setupContext.Doctors.Add(doc);
            await setupContext.SaveChangesAsync();
            pendingDocId = doc.Id;
        }

        // 2. Act: Approve doctor using AdminService on real SQL Server
        var emailMock = new Mock<IEmailService>();
        var clockMock = new Mock<IClinicClock>();

        using (var actContext = new ApplicationDbContext(_options))
        {
            var uow = new UnitOfWork(actContext);
            var adminService = new AdminService(
                uow,
                emailMock.Object,
                clockMock.Object,
                NullLogger<AdminService>.Instance);

            var approveResult = await adminService.ApproveDoctorAsync(pendingDocId);
            approveResult.IsSuccess.Should().BeTrue();
        }

        // 3. Assert: Verify in fresh context from database
        using (var verifyContext = new ApplicationDbContext(_options))
        {
            var approvedDoc = await verifyContext.Doctors.FindAsync(pendingDocId);
            approvedDoc.Should().NotBeNull();
            approvedDoc!.IsApproved.Should().BeTrue();
        }

        // Verify notification email was dispatched
        emailMock.Verify(e => e.SendEmailAsync(
            "pending.doc@test.com",
            It.Is<string>(s => s.Contains("Approved")),
            It.IsAny<string>()), Times.Once);
    }
}
