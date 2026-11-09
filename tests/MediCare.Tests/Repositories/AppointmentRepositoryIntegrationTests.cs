using FluentAssertions;
using MediCare.Data.Context;
using MediCare.Data.Entities;
using MediCare.Data.Enums;
using MediCare.Data.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MediCare.Tests.Repositories;

public class AppointmentRepositoryIntegrationTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly AppointmentRepository _repository;

    public AppointmentRepositoryIntegrationTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new ApplicationDbContext(options);
        _repository = new AppointmentRepository(_context);
    }

    [Theory]
    [InlineData(20, "10:00", "10:20", "10:20", "10:40", false)] // Adjacent 20m -> No conflict
    [InlineData(20, "10:00", "10:20", "10:10", "10:30", true)]  // Overlap 20m -> Conflict
    [InlineData(30, "10:00", "10:30", "10:30", "11:00", false)] // Adjacent 30m -> No conflict
    [InlineData(30, "10:00", "10:30", "10:15", "10:45", true)]  // Overlap 30m -> Conflict
    [InlineData(45, "10:00", "10:45", "10:45", "11:30", false)] // Adjacent 45m -> No conflict
    [InlineData(45, "10:00", "10:45", "10:30", "11:15", true)]  // Overlap 45m -> Conflict
    [InlineData(60, "10:00", "11:00", "11:00", "12:00", false)] // Adjacent 60m -> No conflict
    [InlineData(60, "10:00", "11:00", "10:30", "11:30", true)]  // Overlap 60m -> Conflict
    public async Task HasConflictAsync_VariousSlotDurations_AccuratelyDetectsOverlapAndAdjacent(
        int duration, string existStart, string existEnd, string reqStart, string reqEnd, bool expectedConflict)
    {
        // Arrange
        var targetDate = new DateTime(2026, 11, 15);
        int docId = 100 + duration + (expectedConflict ? 1 : 0);

        var existingAppt = new Appointment
        {
            Id = docId * 10,
            DoctorId = docId,
            PatientId = 1,
            AppointmentDate = targetDate,
            StartTime = TimeSpan.Parse(existStart),
            EndTime = TimeSpan.Parse(existEnd),
            Status = AppointmentStatus.Confirmed
        };
        _context.Appointments.Add(existingAppt);
        await _context.SaveChangesAsync();

        // Act
        var hasConflict = await _repository.HasConflictAsync(
            docId, targetDate, TimeSpan.Parse(reqStart), TimeSpan.Parse(reqEnd));

        // Assert
        hasConflict.Should().Be(expectedConflict);
    }

    [Fact]
    public async Task HasPatientConflictAsync_ShouldRejectOverlappingAppointment_AcrossDifferentDoctors()
    {
        // Arrange: Patient 5 has a 45-min appointment with Doctor 1 from 10:00 to 10:45
        var targetDate = new DateTime(2026, 11, 20);
        var existingWithDoc1 = new Appointment
        {
            Id = 801,
            DoctorId = 1,
            PatientId = 5,
            AppointmentDate = targetDate,
            StartTime = new TimeSpan(10, 0, 0),
            EndTime = new TimeSpan(10, 45, 0),
            Status = AppointmentStatus.Confirmed
        };
        _context.Appointments.Add(existingWithDoc1);
        await _context.SaveChangesAsync();

        // Act 1: Patient attempts to book with Doctor 2 from 10:30 to 11:00 (overlaps with Doctor 1's visit)
        var conflictWithDoc2 = await _repository.HasPatientConflictAsync(
            5, targetDate, new TimeSpan(10, 30, 0), new TimeSpan(11, 0, 0));

        // Act 2: Patient books with Doctor 2 adjacent from 10:45 to 11:15 (back-to-back, no overlap)
        var adjacentWithDoc2 = await _repository.HasPatientConflictAsync(
            5, targetDate, new TimeSpan(10, 45, 0), new TimeSpan(11, 15, 0));

        // Assert
        conflictWithDoc2.Should().BeTrue("Patient cannot have overlapping appointments with two different doctors");
        adjacentWithDoc2.Should().BeFalse("Patient can have back-to-back adjacent appointments");
    }

    [Fact]
    public async Task GetByIdWithDetailsAsync_ShouldIncludeMedicalRecord_WhenRecordExists()
    {
        // Arrange
        var targetDate = new DateTime(2026, 11, 15);
        var doctorUser = new ApplicationUser { Id = "doc_u", FullName = "Dr. Test" };
        var patientUser = new ApplicationUser { Id = "pat_u", FullName = "Patient Test" };
        var spec = new Specialization { Id = 1, Name = "General" };
        var doctor = new Doctor { Id = 10, UserId = "doc_u", SpecializationId = 1, User = doctorUser, Specialization = spec };
        var patient = new Patient { Id = 20, UserId = "pat_u", User = patientUser };

        var appt = new Appointment
        {
            Id = 50,
            DoctorId = 10,
            PatientId = 20,
            AppointmentDate = targetDate,
            StartTime = new TimeSpan(10, 0, 0),
            EndTime = new TimeSpan(10, 30, 0),
            Doctor = doctor,
            Patient = patient
        };

        var record = new MedicalRecord
        {
            Id = 99,
            AppointmentId = 50,
            DoctorId = 10,
            PatientId = 20,
            Diagnosis = "Hypertension Confirmed",
            AttachmentPath = "uploads/records/scan.pdf"
        };

        _context.Specializations.Add(spec);
        _context.Users.AddRange(doctorUser, patientUser);
        _context.Doctors.Add(doctor);
        _context.Patients.Add(patient);
        _context.Appointments.Add(appt);
        _context.MedicalRecords.Add(record);
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        // Act
        var result = await _repository.GetByIdWithDetailsAsync(50);

        // Assert
        result.Should().NotBeNull();
        result!.MedicalRecord.Should().NotBeNull();
        result.MedicalRecord!.Diagnosis.Should().Be("Hypertension Confirmed");
        result.MedicalRecord.AttachmentPath.Should().Be("uploads/records/scan.pdf");
    }

    [Fact]
    public async Task PatientRepository_ShouldAutoIncludeUser_WhenQueryingPatient()
    {
        // Arrange
        var user = new ApplicationUser { Id = "pat_user_auto", FullName = "Sara Ahmed", Email = "sara@patient.com" };
        var patient = new Patient { Id = 35, UserId = "pat_user_auto", User = user };

        _context.Users.Add(user);
        _context.Patients.Add(patient);
        await _context.SaveChangesAsync();

        var patientRepo = new Repository<Patient>(_context);

        // Act
        var fetched = (await patientRepo.FindAsync(p => p.Id == 35)).FirstOrDefault();

        // Assert
        fetched.Should().NotBeNull();
        fetched!.User.Should().NotBeNull();
        fetched.User.Email.Should().Be("sara@patient.com");
    }

    [Fact]
    public async Task GetDoctorAppointmentsAsync_DoesNotIncludePatientOrUser_PreventingOverFetching()
    {
        // Arrange (PERF-04)
        var user = new ApplicationUser { Id = "pat-p4", FullName = "Hassan PII", Email = "pii@patient.com" };
        var patient = new Patient { Id = 40, UserId = "pat-p4", User = user };
        var appt = new Appointment
        {
            Id = 400,
            DoctorId = 20,
            PatientId = 40,
            AppointmentDate = new DateTime(2026, 11, 20),
            StartTime = new TimeSpan(9, 0, 0),
            EndTime = new TimeSpan(9, 30, 0),
            Status = AppointmentStatus.Confirmed
        };

        _context.Users.Add(user);
        _context.Patients.Add(patient);
        _context.Appointments.Add(appt);
        await _context.SaveChangesAsync();

        // Act
        var result = await _repository.GetDoctorAppointmentsAsync(20, new DateTime(2026, 11, 20));

        // Assert
        result.Should().HaveCount(1);
        result[0].Id.Should().Be(400);
        result[0].StartTime.Should().Be(new TimeSpan(9, 0, 0));
        // Verify Patient navigation is null (not loaded), preventing unnecessary joins/over-fetching
        result[0].Patient.Should().BeNull();
    }

    [Fact]
    public async Task GetDoctorAppointmentsWithDetailsAsync_DoesNotIncludeMedicalRecord_PreventingOverFetching()
    {
        // Arrange (PERF-05)
        var doctorUser = new ApplicationUser { Id = "doc-p5", FullName = "Dr. Tarek" };
        var spec = new Specialization { Id = 50, Name = "Orthopedics" };
        var doctor = new Doctor { Id = 50, UserId = "doc-p5", SpecializationId = 50, User = doctorUser, Specialization = spec };
        var patientUser = new ApplicationUser { Id = "pat-p5", FullName = "Patient Five" };
        var patient = new Patient { Id = 50, UserId = "pat-p5", User = patientUser };

        var record = new MedicalRecord
        {
            Id = 50,
            AppointmentId = 500,
            PatientId = 50,
            DoctorId = 50,
            Diagnosis = "Confidential Notes / Huge Payload",
            Symptoms = "Knee pain",
            VisitNotes = "Sensitive history"
        };

        var appt = new Appointment
        {
            Id = 500,
            DoctorId = 50,
            PatientId = 50,
            AppointmentDate = new DateTime(2026, 11, 21),
            StartTime = new TimeSpan(11, 0, 0),
            EndTime = new TimeSpan(11, 30, 0),
            Status = AppointmentStatus.Completed
        };

        _context.Users.AddRange(doctorUser, patientUser);
        _context.Specializations.Add(spec);
        _context.Doctors.Add(doctor);
        _context.Patients.Add(patient);
        _context.MedicalRecords.Add(record);
        _context.Appointments.Add(appt);
        await _context.SaveChangesAsync();

        // Act
        var result = await _repository.GetDoctorAppointmentsWithDetailsAsync(50, AppointmentStatus.Completed, new DateTime(2026, 11, 21));

        // Assert
        result.Should().HaveCount(1);
        result[0].Id.Should().Be(500);
        result[0].Doctor.Should().NotBeNull();
        result[0].Doctor!.User.Should().NotBeNull();
        result[0].Patient.Should().NotBeNull();
        result[0].Patient!.User.Should().NotBeNull();
        // MedicalRecord is not loaded, saving memory and eliminating unneeded heavy text payload
        result[0].MedicalRecord.Should().BeNull();
    }

    [Fact]
    public async Task Repository_AnyAsync_And_FirstOrDefaultAsync_WorkAccurately()
    {
        // Arrange (PERF-06)
        var spec1 = new Specialization { Id = 61, Name = "Endocrinology" };
        var spec2 = new Specialization { Id = 62, Name = "Rheumatology" };
        _context.Specializations.AddRange(spec1, spec2);
        await _context.SaveChangesAsync();

        var repo = new Repository<Specialization>(_context);

        // Act & Assert AnyAsync
        var exists = await repo.AnyAsync(s => s.Name == "Endocrinology");
        var notExists = await repo.AnyAsync(s => s.Name == "NonExistentSpecialty");
        exists.Should().BeTrue();
        notExists.Should().BeFalse();

        // Act & Assert FirstOrDefaultAsync
        var found = await repo.FirstOrDefaultAsync(s => s.Name == "Rheumatology");
        var notFound = await repo.FirstOrDefaultAsync(s => s.Name == "NonExistentSpecialty");
        found.Should().NotBeNull();
        found!.Id.Should().Be(62);
        notFound.Should().BeNull();
    }

    [Fact]
    public async Task GetDashboardMetricsSummaryAsync_AggregatesKpisTrendsAndDemographics_WithoutMaterializingEntities()
    {
        // Arrange (PERF-01 Database Aggregation)
        var clinicToday = new DateTime(2026, 11, 15);

        // Specializations
        var specCardio = new Specialization { Id = 71, Name = "Cardiology" };
        var specDerma = new Specialization { Id = 72, Name = "Dermatology" };
        _context.Specializations.AddRange(specCardio, specDerma);

        // Doctors
        var userDoc1 = new ApplicationUser { Id = "u-doc-1", FullName = "Dr. Cardio One" };
        var userDoc2 = new ApplicationUser { Id = "u-doc-2", FullName = "Dr. Derma Two" };
        var userDoc3 = new ApplicationUser { Id = "u-doc-3", FullName = "Dr. Pending Three" };
        _context.Users.AddRange(userDoc1, userDoc2, userDoc3);

        var doc1 = new Doctor { Id = 71, UserId = "u-doc-1", SpecializationId = 71, IsApproved = true };
        var doc2 = new Doctor { Id = 72, UserId = "u-doc-2", SpecializationId = 72, IsApproved = true };
        var doc3 = new Doctor { Id = 73, UserId = "u-doc-3", SpecializationId = 71, IsApproved = false };
        _context.Doctors.AddRange(doc1, doc2, doc3);

        // Patients (testing demographic age buckets relative to 2026-11-15)
        var userPat1 = new ApplicationUser { Id = "u-pat-1", FullName = "Child Pat" };
        var userPat2 = new ApplicationUser { Id = "u-pat-2", FullName = "Young Pat" };
        var userPat3 = new ApplicationUser { Id = "u-pat-3", FullName = "Middle Pat" };
        var userPat4 = new ApplicationUser { Id = "u-pat-4", FullName = "Senior Pat" };
        _context.Users.AddRange(userPat1, userPat2, userPat3, userPat4);

        var pat1 = new Patient { Id = 71, UserId = "u-pat-1", Gender = "Male", DateOfBirth = new DateTime(2016, 5, 1) }; // Age ~10 (<18)
        var pat2 = new Patient { Id = 72, UserId = "u-pat-2", Gender = "Female", DateOfBirth = new DateTime(2000, 1, 1) }; // Age 26 (18..35)
        var pat3 = new Patient { Id = 73, UserId = "u-pat-3", Gender = "Male", DateOfBirth = new DateTime(1982, 3, 1) }; // Age 44 (36..50)
        var pat4 = new Patient { Id = 74, UserId = "u-pat-4", Gender = "Female", DateOfBirth = new DateTime(1960, 8, 1) }; // Age 66 (>50)
        _context.Patients.AddRange(pat1, pat2, pat3, pat4);

        // Appointments
        // 1. Paid with explicit PaidAmount = 450 (ConsultationFee 500, Discount 50)
        var appt1 = new Appointment
        {
            Id = 701, DoctorId = 71, PatientId = 71,
            AppointmentDate = new DateTime(2026, 11, 10),
            Status = AppointmentStatus.Completed,
            PaymentStatus = PaymentStatus.Paid,
            ConsultationFee = 500m,
            DiscountAmount = 50m,
            PaidAmount = 450m
        };

        // 2. Paid with legacy record (PaidAmount null, DiscountAmount = 100, Fee = 400 -> Collected = 300)
        var appt2 = new Appointment
        {
            Id = 702, DoctorId = 71, PatientId = 72,
            AppointmentDate = new DateTime(2026, 11, 11),
            Status = AppointmentStatus.Completed,
            PaymentStatus = PaymentStatus.Paid,
            ConsultationFee = 400m,
            DiscountAmount = 100m,
            PaidAmount = null
        };

        // 3. Refunded appointment (PaidAmount null, Fee = 350, Discount = 50 -> Collected/Refunded = 300)
        var appt3 = new Appointment
        {
            Id = 703, DoctorId = 72, PatientId = 73,
            AppointmentDate = new DateTime(2026, 11, 12),
            Status = AppointmentStatus.Cancelled,
            PaymentStatus = PaymentStatus.Refunded,
            ConsultationFee = 350m,
            DiscountAmount = 50m,
            PaidAmount = null
        };

        // 4. Unpaid confirmed appointment (Fee = 250 -> Pending Revenue)
        var appt4 = new Appointment
        {
            Id = 704, DoctorId = 72, PatientId = 74,
            AppointmentDate = new DateTime(2026, 11, 13),
            Status = AppointmentStatus.Confirmed,
            PaymentStatus = PaymentStatus.Unpaid,
            ConsultationFee = 250m
        };

        // 5. Unpaid cancelled appointment (Fee = 300 -> MUST BE EXCLUDED from Pending Revenue)
        var appt5 = new Appointment
        {
            Id = 705, DoctorId = 72, PatientId = 71,
            AppointmentDate = new DateTime(2026, 11, 14),
            Status = AppointmentStatus.Cancelled,
            PaymentStatus = PaymentStatus.Unpaid,
            ConsultationFee = 300m
        };

        // 6. Paid legacy record without discount (PaidAmount null, DiscountAmount null, Fee = 200 -> Collected = 200)
        var appt6 = new Appointment
        {
            Id = 706, DoctorId = 71, PatientId = 73,
            AppointmentDate = new DateTime(2026, 11, 15),
            Status = AppointmentStatus.Completed,
            PaymentStatus = PaymentStatus.Paid,
            ConsultationFee = 200m,
            DiscountAmount = null,
            PaidAmount = null
        };

        _context.Appointments.AddRange(appt1, appt2, appt3, appt4, appt5, appt6);
        await _context.SaveChangesAsync();

        // Act
        var summary = await _repository.GetDashboardMetricsSummaryAsync(clinicToday);

        // Assert
        summary.Should().NotBeNull();
        summary!.TotalAppointments.Should().Be(6);
        summary.ActiveDoctorsCount.Should().Be(2);
        summary.PendingDoctorsCount.Should().Be(1);
        summary.CompletedVisitsCount.Should().Be(3);

        // Paid: 450 + 300 + 200 = 950
        summary.TotalRevenueCollected.Should().Be(950m);
        // Refunded: 300
        summary.TotalRefundedRevenue.Should().Be(300m);
        summary.RefundedAppointmentsCount.Should().Be(1);
        // Pending: only appt4 (250m), appt5 (cancelled) excluded
        summary.TotalPendingRevenue.Should().Be(250m);

        // Demographics
        summary.Demographics.TotalPatients.Should().Be(4);
        summary.Demographics.MaleCount.Should().Be(2);
        summary.Demographics.FemaleCount.Should().Be(2);
        summary.Demographics.AgeUnder18Count.Should().Be(1);
        summary.Demographics.Age18To35Count.Should().Be(1);
        summary.Demographics.Age36To50Count.Should().Be(1);
        summary.Demographics.AgeOver50Count.Should().Be(1);

        // Monthly trends: 12 months present, current month (Nov 2026) has metrics
        summary.MonthlyTrends.Should().HaveCount(12);
        var novTrend = summary.MonthlyTrends.First(t => t.Year == 2026 && t.Month == 11);
        novTrend.CompletedCount.Should().Be(3);
        novTrend.CancelledCount.Should().Be(2); // appt3 and appt5
        novTrend.PaidRevenue.Should().Be(950m);
        novTrend.RefundedRevenue.Should().Be(300m);
        novTrend.RefundedCount.Should().Be(1);
        novTrend.UnpaidRevenue.Should().Be(550m); // appt4 (250) + appt5 (300)

        // Top Doctors
        summary.TopDoctors.Should().NotBeEmpty();
        summary.TopDoctors.First().DoctorId.Should().Be(71); // Doc 71 now has 3 appointments (appt1, appt2, appt6) with 950m revenue
        summary.TopDoctors.First().TotalAppointments.Should().Be(3);
        summary.TopDoctors.First().TotalRevenue.Should().Be(950m);

        // Specializations
        summary.Specializations.Should().HaveCount(2);
        var cardioSpec = summary.Specializations.First(s => s.SpecializationName == "Cardiology");
        cardioSpec.AppointmentCount.Should().Be(3); // Doc 71 has appt1, appt2, appt6
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }
}
