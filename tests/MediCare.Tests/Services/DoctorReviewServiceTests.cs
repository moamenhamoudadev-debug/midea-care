using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using MediCare.Data.Context;
using MediCare.Data.Entities;
using MediCare.Data.Enums;
using MediCare.Services.Contracts;
using MediCare.Services.DTOs;
using MediCare.Services.Implementations;
using MediCare.Services.Validators;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace MediCare.Tests.Services;

public class DoctorReviewServiceTests
{
    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        return new ApplicationDbContext(options);
    }

    private static (Doctor doctor, Patient patient, Appointment completedAppt) SeedBasicData(ApplicationDbContext context)
    {
        var doctorUser = new ApplicationUser
        {
            Id = "doc-user-1",
            FullName = "Dr. Hazem El-Masry",
            Email = "hazem@medicare.com",
            UserName = "hazem@medicare.com"
        };
        var patientUser = new ApplicationUser
        {
            Id = "pat-user-1",
            FullName = "Ahmed Hassan",
            Email = "ahmed@medicare.com",
            UserName = "ahmed@medicare.com"
        };

        context.Users.AddRange(doctorUser, patientUser);

        var spec = new Specialization { Id = 1, Name = "Cardiology" };
        context.Specializations.Add(spec);

        var doctor = new Doctor
        {
            Id = 1,
            UserId = doctorUser.Id,
            User = doctorUser,
            SpecializationId = 1,
            Specialization = spec,
            LicenseNumber = "DOC-12345",
            ConsultationFee = 400,
            SlotDurationMinutes = 30,
            IsApproved = true
        };
        context.Doctors.Add(doctor);

        var patient = new Patient
        {
            Id = 1,
            UserId = patientUser.Id,
            User = patientUser,
            DateOfBirth = new DateTime(1990, 5, 12),
            Gender = "Male"
        };
        context.Patients.Add(patient);

        var completedAppt = new Appointment
        {
            Id = 101,
            DoctorId = 1,
            Doctor = doctor,
            PatientId = 1,
            Patient = patient,
            AppointmentDate = DateTime.Today.AddDays(-5),
            StartTime = new TimeSpan(10, 0, 0),
            EndTime = new TimeSpan(10, 30, 0),
            Status = AppointmentStatus.Completed
        };
        context.Appointments.Add(completedAppt);

        context.SaveChanges();

        return (doctor, patient, completedAppt);
    }

    [Fact]
    public async Task SubmitReviewAsync_WithValidCompletedAppointment_CreatesVerifiedReview()
    {
        // Arrange
        using var context = CreateDbContext();
        var (doctor, patient, appt) = SeedBasicData(context);

        var validator = new SubmitDoctorReviewValidator();
        var sut = new DoctorReviewService(context, NullLogger<DoctorReviewService>.Instance, null, null, validator);

        var dto = new SubmitDoctorReviewDto
        {
            DoctorId = doctor.Id,
            AppointmentId = appt.Id,
            Rating = 5,
            Title = "طبيب متميز ومهني",
            Comment = "تجربة ممتازة جداً واستمع لجميع الأعراض ببالغ الاهتمام والتشخيص دقيق."
        };

        // Act
        var result = await sut.SubmitReviewAsync(dto, patient.UserId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.DoctorId.Should().Be(doctor.Id);
        result.Value.Rating.Should().Be(5);
        result.Value.IsVerifiedAppointment.Should().BeTrue();
        result.Value.IsDemo.Should().BeFalse();
        result.Value.ModerationStatus.Should().Be(ReviewModerationStatus.Approved);
        result.Value.PatientDisplayName.Should().Be("Ahmed H.");

        var savedReview = await context.DoctorReviews.FirstOrDefaultAsync(r => r.AppointmentId == appt.Id);
        savedReview.Should().NotBeNull();
        savedReview!.Rating.Should().Be(5);
        savedReview.IsVerifiedAppointment.Should().BeTrue();
    }

    [Fact]
    public async Task SubmitReviewAsync_WhenAppointmentNotCompleted_FailsWithEligiblityError()
    {
        // Arrange
        using var context = CreateDbContext();
        var (doctor, patient, appt) = SeedBasicData(context);
        appt.Status = AppointmentStatus.Confirmed; // Not completed!
        context.SaveChanges();

        var validator = new SubmitDoctorReviewValidator();
        var sut = new DoctorReviewService(context, NullLogger<DoctorReviewService>.Instance, null, null, validator);

        var dto = new SubmitDoctorReviewDto
        {
            DoctorId = doctor.Id,
            AppointmentId = appt.Id,
            Rating = 4,
            Comment = "Great initial consultation scheduled."
        };

        // Act
        var result = await sut.SubmitReviewAsync(dto, patient.UserId);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("completed appointments");
    }

    [Fact]
    public async Task SubmitReviewAsync_WhenAppointmentAlreadyReviewed_PreventsDuplicate()
    {
        // Arrange
        using var context = CreateDbContext();
        var (doctor, patient, appt) = SeedBasicData(context);

        var validator = new SubmitDoctorReviewValidator();
        var sut = new DoctorReviewService(context, NullLogger<DoctorReviewService>.Instance, null, null, validator);

        var dto1 = new SubmitDoctorReviewDto
        {
            DoctorId = doctor.Id,
            AppointmentId = appt.Id,
            Rating = 5,
            Comment = "First authentic review."
        };

        var firstResult = await sut.SubmitReviewAsync(dto1, patient.UserId);
        firstResult.IsSuccess.Should().BeTrue();

        var dto2 = new SubmitDoctorReviewDto
        {
            DoctorId = doctor.Id,
            AppointmentId = appt.Id,
            Rating = 3,
            Comment = "Attempted duplicate review for the same appointment."
        };

        // Act
        var secondResult = await sut.SubmitReviewAsync(dto2, patient.UserId);

        // Assert
        secondResult.IsSuccess.Should().BeFalse();
        secondResult.Error.Should().Contain("already been submitted");
    }

    [Fact]
    public async Task SubmitReviewAsync_WhenAppointmentBelongsToDifferentPatient_Fails()
    {
        // Arrange
        using var context = CreateDbContext();
        var (doctor, patient, appt) = SeedBasicData(context);

        // Another patient
        var otherUser = new ApplicationUser { Id = "pat-user-2", FullName = "Other User", UserName = "other@medicare.com" };
        var otherPatient = new Patient { Id = 2, UserId = otherUser.Id, User = otherUser, Gender = "Female", DateOfBirth = new DateTime(1995, 1, 1) };
        context.Users.Add(otherUser);
        context.Patients.Add(otherPatient);
        context.SaveChanges();

        var validator = new SubmitDoctorReviewValidator();
        var sut = new DoctorReviewService(context, NullLogger<DoctorReviewService>.Instance, null, null, validator);

        var dto = new SubmitDoctorReviewDto
        {
            DoctorId = doctor.Id,
            AppointmentId = appt.Id, // belongs to patient 1, not patient 2
            Rating = 4,
            Comment = "Trying to review another user's visit."
        };

        // Act
        var result = await sut.SubmitReviewAsync(dto, otherPatient.UserId);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("belong to your account");
    }

    [Fact]
    public async Task SubmitReviewAsync_WithScriptTagsInComment_FailsValidation()
    {
        // Arrange
        using var context = CreateDbContext();
        var (doctor, patient, appt) = SeedBasicData(context);

        var validator = new SubmitDoctorReviewValidator();
        var sut = new DoctorReviewService(context, NullLogger<DoctorReviewService>.Instance, null, null, validator);

        var dto = new SubmitDoctorReviewDto
        {
            DoctorId = doctor.Id,
            AppointmentId = appt.Id,
            Rating = 5,
            Comment = "<script>alert('xss')</script> Malicious payload."
        };

        // Act
        var result = await sut.SubmitReviewAsync(dto, patient.UserId);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("invalid characters or markup");
    }

    [Fact]
    public async Task GetDoctorRatingSummaryAsync_WithMultipleReviews_CalculatesCorrectAverageAndBreakdown()
    {
        // Arrange
        using var context = CreateDbContext();
        var (doctor, _, _) = SeedBasicData(context);

        // Add 4 reviews: 5, 5, 4, 2 stars (Average = 16 / 4 = 4.0)
        context.DoctorReviews.AddRange(
            new DoctorReview { DoctorId = doctor.Id, Rating = 5, Comment = "Review 1", PatientDisplayName = "P1", ModerationStatus = ReviewModerationStatus.Approved, IsVerifiedAppointment = true, CreatedAt = DateTime.UtcNow },
            new DoctorReview { DoctorId = doctor.Id, Rating = 5, Comment = "Review 2", PatientDisplayName = "P2", ModerationStatus = ReviewModerationStatus.Approved, IsVerifiedAppointment = true, CreatedAt = DateTime.UtcNow },
            new DoctorReview { DoctorId = doctor.Id, Rating = 4, Comment = "Review 3", PatientDisplayName = "P3", ModerationStatus = ReviewModerationStatus.Approved, IsVerifiedAppointment = false, CreatedAt = DateTime.UtcNow },
            new DoctorReview { DoctorId = doctor.Id, Rating = 2, Comment = "Review 4", PatientDisplayName = "P4", ModerationStatus = ReviewModerationStatus.Approved, IsVerifiedAppointment = false, CreatedAt = DateTime.UtcNow }
        );
        context.SaveChanges();

        var sut = new DoctorReviewService(context, NullLogger<DoctorReviewService>.Instance);

        // Act
        var summary = await sut.GetDoctorRatingSummaryAsync(doctor.Id);

        // Assert
        summary.Should().NotBeNull();
        summary.DoctorId.Should().Be(doctor.Id);
        summary.TotalReviewsCount.Should().Be(4);
        summary.VerifiedReviewsCount.Should().Be(2);
        summary.AverageRating.Should().Be(4.0);
        summary.FiveStarCount.Should().Be(2);
        summary.FourStarCount.Should().Be(1);
        summary.ThreeStarCount.Should().Be(0);
        summary.TwoStarCount.Should().Be(1);
        summary.OneStarCount.Should().Be(0);

        summary.FiveStarPercentage.Should().Be(50);
        summary.FourStarPercentage.Should().Be(25);
        summary.TwoStarPercentage.Should().Be(25);
    }

    [Fact]
    public async Task GetDoctorRatingSummaryAsync_WithNoReviews_ReturnsZerosSafely()
    {
        // Arrange
        using var context = CreateDbContext();
        var (doctor, _, _) = SeedBasicData(context);

        var sut = new DoctorReviewService(context, NullLogger<DoctorReviewService>.Instance);

        // Act
        var summary = await sut.GetDoctorRatingSummaryAsync(doctor.Id);

        // Assert
        summary.Should().NotBeNull();
        summary.AverageRating.Should().Be(0.0);
        summary.TotalReviewsCount.Should().Be(0);
        summary.FiveStarPercentage.Should().Be(0);
    }

    [Fact]
    public async Task GetDoctorRatingSummaryAsync_ExcludesHiddenReviews()
    {
        // Arrange
        using var context = CreateDbContext();
        var (doctor, _, _) = SeedBasicData(context);

        // 1 approved 5-star review, 1 hidden 1-star review
        context.DoctorReviews.AddRange(
            new DoctorReview { DoctorId = doctor.Id, Rating = 5, Comment = "Good", PatientDisplayName = "P1", ModerationStatus = ReviewModerationStatus.Approved, CreatedAt = DateTime.UtcNow },
            new DoctorReview { DoctorId = doctor.Id, Rating = 1, Comment = "Bad", PatientDisplayName = "P2", ModerationStatus = ReviewModerationStatus.Hidden, CreatedAt = DateTime.UtcNow }
        );
        context.SaveChanges();

        var sut = new DoctorReviewService(context, NullLogger<DoctorReviewService>.Instance);

        // Act
        var summary = await sut.GetDoctorRatingSummaryAsync(doctor.Id);

        // Assert: only the approved 5-star review is counted!
        summary.TotalReviewsCount.Should().Be(1);
        summary.AverageRating.Should().Be(5.0);
        summary.OneStarCount.Should().Be(0);
    }

    [Fact]
    public async Task GetDoctorRatingSummaryAsync_InProductionMode_StrictlyExcludesDemoReviews()
    {
        // Arrange
        using var context = CreateDbContext();
        var (doctor, _, _) = SeedBasicData(context);

        // 1 verified real patient review (5 stars) + 10 demo reviews (various ratings)
        context.DoctorReviews.Add(new DoctorReview
        {
            DoctorId = doctor.Id,
            Rating = 5,
            Comment = "Real patient review",
            PatientDisplayName = "Tarek M.",
            IsDemo = false,
            IsVerifiedAppointment = true,
            ModerationStatus = ReviewModerationStatus.Approved,
            CreatedAt = DateTime.UtcNow
        });

        for (int i = 1; i <= 5; i++)
        {
            context.DoctorReviews.Add(new DoctorReview
            {
                DoctorId = doctor.Id,
                Rating = 1, // Demo low rating
                Comment = "Demo review",
                PatientDisplayName = $"Demo {i}",
                IsDemo = true,
                IsVerifiedAppointment = false,
                ModerationStatus = ReviewModerationStatus.Approved,
                CreatedAt = DateTime.UtcNow
            });
        }
        context.SaveChanges();

        // Production environment mock (IsDevelopment() == false)
        var mockEnv = new Mock<IHostEnvironment>();
        mockEnv.Setup(e => e.EnvironmentName).Returns(Environments.Production);

        var config = new ConfigurationBuilder().Build();
        var sut = new DoctorReviewService(context, NullLogger<DoctorReviewService>.Instance, mockEnv.Object, config);

        // Act
        var summary = await sut.GetDoctorRatingSummaryAsync(doctor.Id);

        // Assert: Demo reviews must NOT contaminate production rating!
        summary.TotalReviewsCount.Should().Be(1);
        summary.AverageRating.Should().Be(5.0);
        summary.OneStarCount.Should().Be(0);
    }

    [Fact]
    public async Task GetDoctorReviewsAsync_FilterByRating_ReturnsOnlyMatchingStars()
    {
        // Arrange
        using var context = CreateDbContext();
        var (doctor, _, _) = SeedBasicData(context);

        context.DoctorReviews.AddRange(
            new DoctorReview { DoctorId = doctor.Id, Rating = 5, Comment = "Review 5", PatientDisplayName = "A", ModerationStatus = ReviewModerationStatus.Approved, CreatedAt = DateTime.UtcNow.AddDays(-1) },
            new DoctorReview { DoctorId = doctor.Id, Rating = 4, Comment = "Review 4", PatientDisplayName = "B", ModerationStatus = ReviewModerationStatus.Approved, CreatedAt = DateTime.UtcNow.AddDays(-2) },
            new DoctorReview { DoctorId = doctor.Id, Rating = 3, Comment = "Review 3", PatientDisplayName = "C", ModerationStatus = ReviewModerationStatus.Approved, CreatedAt = DateTime.UtcNow.AddDays(-3) }
        );
        context.SaveChanges();

        // Allow demo/test reading
        var mockEnv = new Mock<IHostEnvironment>();
        mockEnv.Setup(e => e.EnvironmentName).Returns(Environments.Development);
        var sut = new DoctorReviewService(context, NullLogger<DoctorReviewService>.Instance, mockEnv.Object);

        // Act: Filter by 4 stars
        var result = await sut.GetDoctorReviewsAsync(new DoctorReviewsQueryDto
        {
            DoctorId = doctor.Id,
            RatingFilter = 4
        });

        // Assert
        result.TotalCount.Should().Be(1);
        result.Items.Should().ContainSingle();
        result.Items[0].Rating.Should().Be(4);
    }

    [Fact]
    public async Task ToggleHelpfulVoteAsync_TogglesVoteAndMaintainsAccurateCount()
    {
        // Arrange
        using var context = CreateDbContext();
        var (doctor, patient, _) = SeedBasicData(context);

        var review = new DoctorReview
        {
            DoctorId = doctor.Id,
            Rating = 5,
            Comment = "Excellent doctor",
            PatientDisplayName = "Patient",
            HelpfulCount = 0,
            ModerationStatus = ReviewModerationStatus.Approved,
            CreatedAt = DateTime.UtcNow
        };
        context.DoctorReviews.Add(review);
        context.SaveChanges();

        var sut = new DoctorReviewService(context, NullLogger<DoctorReviewService>.Instance);

        // Act 1: First vote
        var vote1 = await sut.ToggleHelpfulVoteAsync(review.Id, patient.UserId, "127.0.0.1");

        // Assert 1
        vote1.IsSuccess.Should().BeTrue();
        vote1.Value!.HasVoted.Should().BeTrue();
        vote1.Value.HelpfulCount.Should().Be(1);

        var dbReview = await context.DoctorReviews.FindAsync(review.Id);
        dbReview!.HelpfulCount.Should().Be(1);

        // Act 2: Second vote by same user (toggle off)
        var vote2 = await sut.ToggleHelpfulVoteAsync(review.Id, patient.UserId, "127.0.0.1");

        // Assert 2
        vote2.IsSuccess.Should().BeTrue();
        vote2.Value!.HasVoted.Should().BeFalse();
        vote2.Value.HelpfulCount.Should().Be(0);

        dbReview = await context.DoctorReviews.FindAsync(review.Id);
        dbReview!.HelpfulCount.Should().Be(0);
    }

    [Fact]
    public async Task DoctorReplyAsync_WhenValidDoctor_SetsResponseAndDate()
    {
        // Arrange
        using var context = CreateDbContext();
        var (doctor, _, _) = SeedBasicData(context);

        var review = new DoctorReview
        {
            DoctorId = doctor.Id,
            Rating = 5,
            Comment = "Great experience",
            PatientDisplayName = "Patient",
            ModerationStatus = ReviewModerationStatus.Approved,
            CreatedAt = DateTime.UtcNow
        };
        context.DoctorReviews.Add(review);
        context.SaveChanges();

        var sut = new DoctorReviewService(context, NullLogger<DoctorReviewService>.Instance);

        var dto = new DoctorReplyReviewDto
        {
            ReviewId = review.Id,
            Response = "Thank you very much for your kind words! Wishing you health."
        };

        // Act
        var result = await sut.DoctorReplyAsync(dto, doctor.UserId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.DoctorResponse.Should().Be(dto.Response);
        result.Value.DoctorResponseDateUtc.Should().NotBeNull();

        var updated = await context.DoctorReviews.FindAsync(review.Id);
        updated!.DoctorResponse.Should().Be(dto.Response);
        updated.DoctorResponseDateUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task DoctorReplyAsync_WhenDifferentDoctor_FailsWithAuthorizationError()
    {
        // Arrange
        using var context = CreateDbContext();
        var (doctor, _, _) = SeedBasicData(context);

        // Another doctor
        var otherDocUser = new ApplicationUser { Id = "doc-user-2", FullName = "Dr. Other", UserName = "otherdoc@medicare.com" };
        var otherDoctor = new Doctor { Id = 2, UserId = otherDocUser.Id, User = otherDocUser, SpecializationId = 1, LicenseNumber = "DOC-99999", ConsultationFee = 350, SlotDurationMinutes = 30, IsApproved = true };
        context.Users.Add(otherDocUser);
        context.Doctors.Add(otherDoctor);

        var review = new DoctorReview
        {
            DoctorId = doctor.Id,
            Rating = 5,
            Comment = "Doctor 1 review",
            PatientDisplayName = "Patient",
            ModerationStatus = ReviewModerationStatus.Approved,
            CreatedAt = DateTime.UtcNow
        };
        context.DoctorReviews.Add(review);
        context.SaveChanges();

        var sut = new DoctorReviewService(context, NullLogger<DoctorReviewService>.Instance);

        var dto = new DoctorReplyReviewDto
        {
            ReviewId = review.Id,
            Response = "Attempting to reply to another doctor's review."
        };

        // Act
        var result = await sut.DoctorReplyAsync(dto, otherDoctor.UserId);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("own profile");
    }

    [Fact]
    public async Task AdminModerateReviewAsync_HideReview_RequiresReason()
    {
        // Arrange
        using var context = CreateDbContext();
        var (doctor, _, _) = SeedBasicData(context);

        var review = new DoctorReview
        {
            DoctorId = doctor.Id,
            Rating = 1,
            Comment = "Negative review",
            PatientDisplayName = "Patient",
            ModerationStatus = ReviewModerationStatus.Approved,
            CreatedAt = DateTime.UtcNow
        };
        context.DoctorReviews.Add(review);
        context.SaveChanges();

        var sut = new DoctorReviewService(context, NullLogger<DoctorReviewService>.Instance);

        var dto = new AdminModerateReviewDto
        {
            ReviewId = review.Id,
            NewStatus = ReviewModerationStatus.Hidden,
            Reason = "" // Missing reason!
        };

        // Act
        var result = await sut.AdminModerateReviewAsync(dto, "admin-1");

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("moderation reason");
    }

    [Fact]
    public async Task AdminModerateReviewAsync_HideReview_WithValidReason_SavesAuditFields()
    {
        // Arrange
        using var context = CreateDbContext();
        var (doctor, _, _) = SeedBasicData(context);

        var review = new DoctorReview
        {
            DoctorId = doctor.Id,
            Rating = 1,
            Comment = "Review violating community standards",
            PatientDisplayName = "Patient",
            ModerationStatus = ReviewModerationStatus.Approved,
            CreatedAt = DateTime.UtcNow
        };
        context.DoctorReviews.Add(review);
        context.SaveChanges();

        var sut = new DoctorReviewService(context, NullLogger<DoctorReviewService>.Instance);

        var dto = new AdminModerateReviewDto
        {
            ReviewId = review.Id,
            NewStatus = ReviewModerationStatus.Hidden,
            Reason = "Contains offensive profanity violating content guidelines."
        };

        // Act
        var result = await sut.AdminModerateReviewAsync(dto, "admin-user-id");

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.ModerationStatus.Should().Be(ReviewModerationStatus.Hidden);

        var updated = await context.DoctorReviews.FindAsync(review.Id);
        updated!.ModerationStatus.Should().Be(ReviewModerationStatus.Hidden);
        updated.ModeratedByAdminId.Should().Be("admin-user-id");
        updated.ModeratedAtUtc.Should().NotBeNull();
        updated.ModerationReason.Should().Be(dto.Reason);
    }

    [Fact]
    public async Task SeedDemoReviewsAsync_InDevelopment_SeedsRealisticReviewsAndIsIdempotent()
    {
        // Arrange
        using var context = CreateDbContext();
        var (doctor, _, _) = SeedBasicData(context);

        var mockEnv = new Mock<IHostEnvironment>();
        mockEnv.Setup(e => e.EnvironmentName).Returns(Environments.Development);

        var sut = new DoctorReviewService(context, NullLogger<DoctorReviewService>.Instance, mockEnv.Object);

        // Act 1: First seed
        var seededCount1 = await sut.SeedDemoReviewsAsync();

        // Assert 1
        seededCount1.Should().BeGreaterThanOrEqualTo(8);
        var totalDemoReviews = await context.DoctorReviews.CountAsync(r => r.DoctorId == doctor.Id && r.IsDemo);
        totalDemoReviews.Should().Be(seededCount1);

        // All seeded reviews must be flagged as demo and unverified
        var demoReviews = await context.DoctorReviews.Where(r => r.IsDemo).ToListAsync();
        demoReviews.Should().AllSatisfy(r =>
        {
            r.IsDemo.Should().BeTrue();
            r.IsVerifiedAppointment.Should().BeFalse();
            r.DoctorId.Should().Be(doctor.Id);
            r.Rating.Should().BeInRange(1, 5);
        });

        // Act 2: Idempotent re-run
        var seededCount2 = await sut.SeedDemoReviewsAsync();

        // Assert 2: Should not duplicate
        seededCount2.Should().Be(0);
        var totalDemoReviewsAfter = await context.DoctorReviews.CountAsync(r => r.DoctorId == doctor.Id && r.IsDemo);
        totalDemoReviewsAfter.Should().Be(totalDemoReviews);
    }

    [Fact]
    public async Task PurgeDemoReviewsAsync_RemovesOnlyDemoReviewsAndPreservesAuthenticReviews()
    {
        // Arrange
        using var context = CreateDbContext();
        var (doctor, patient, appt) = SeedBasicData(context);

        // 1 authentic patient review
        var realReview = new DoctorReview
        {
            DoctorId = doctor.Id,
            PatientId = patient.Id,
            AppointmentId = appt.Id,
            Rating = 5,
            Comment = "Authentic patient review",
            PatientDisplayName = "Ahmed H.",
            IsDemo = false,
            IsVerifiedAppointment = true,
            ModerationStatus = ReviewModerationStatus.Approved,
            CreatedAt = DateTime.UtcNow
        };
        context.DoctorReviews.Add(realReview);

        // 5 demo reviews
        for (int i = 1; i <= 5; i++)
        {
            context.DoctorReviews.Add(new DoctorReview
            {
                DoctorId = doctor.Id,
                Rating = 4,
                Comment = $"Demo review {i}",
                PatientDisplayName = $"Demo Patient {i}",
                IsDemo = true,
                IsVerifiedAppointment = false,
                ModerationStatus = ReviewModerationStatus.Approved,
                CreatedAt = DateTime.UtcNow
            });
        }
        context.SaveChanges();

        var sut = new DoctorReviewService(context, NullLogger<DoctorReviewService>.Instance);

        // Act
        var purgedCount = await sut.PurgeDemoReviewsAsync();

        // Assert
        purgedCount.Should().Be(5);
        var remainingReviews = await context.DoctorReviews.ToListAsync();
        remainingReviews.Should().ContainSingle();
        remainingReviews[0].Id.Should().Be(realReview.Id);
        remainingReviews[0].IsDemo.Should().BeFalse();
        remainingReviews[0].IsVerifiedAppointment.Should().BeTrue();
    }
}
