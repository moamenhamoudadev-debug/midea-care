using MediCare.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediCare.Data.Configurations;

public class DoctorReviewConfiguration : IEntityTypeConfiguration<DoctorReview>
{
    public void Configure(EntityTypeBuilder<DoctorReview> builder)
    {
        builder.ToTable("DoctorReviews", t =>
        {
            t.HasCheckConstraint("CK_DoctorReviews_Rating", "[Rating] >= 1 AND [Rating] <= 5");
        });

        builder.HasKey(r => r.Id);

        builder.Property(r => r.DoctorId)
            .IsRequired();

        builder.Property(r => r.Rating)
            .IsRequired();

        builder.Property(r => r.Title)
            .HasMaxLength(100);

        builder.Property(r => r.Comment)
            .IsRequired()
            .HasMaxLength(2000);

        builder.Property(r => r.PatientDisplayName)
            .IsRequired()
            .HasMaxLength(150);

        builder.Property(r => r.IsVerifiedAppointment)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(r => r.IsDemo)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(r => r.ModerationStatus)
            .IsRequired()
            .HasConversion<int>()
            .HasDefaultValue(MediCare.Data.Enums.ReviewModerationStatus.Approved);

        builder.Property(r => r.HelpfulCount)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(r => r.DoctorResponse)
            .HasMaxLength(2000);

        builder.Property(r => r.ModeratedByAdminId)
            .HasMaxLength(450);

        builder.Property(r => r.ModerationReason)
            .HasMaxLength(500);

        // Relationships
        builder.HasOne(r => r.Doctor)
            .WithMany(d => d.Reviews)
            .HasForeignKey(r => r.DoctorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.Patient)
            .WithMany(p => p.Reviews)
            .HasForeignKey(r => r.PatientId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.Appointment)
            .WithOne(a => a.Review)
            .HasForeignKey<DoctorReview>(r => r.AppointmentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.ModeratedByAdmin)
            .WithMany()
            .HasForeignKey(r => r.ModeratedByAdminId)
            .OnDelete(DeleteBehavior.Restrict);

        // Indexes
        builder.HasIndex(r => new { r.DoctorId, r.ModerationStatus, r.CreatedAt })
            .HasDatabaseName("IX_DoctorReviews_DoctorId_ModerationStatus_CreatedAt");

        builder.HasIndex(r => r.AppointmentId)
            .IsUnique()
            .HasFilter("[AppointmentId] IS NOT NULL")
            .HasDatabaseName("IX_DoctorReviews_AppointmentId");

        builder.HasIndex(r => new { r.DoctorId, r.IsDemo, r.ModerationStatus })
            .HasDatabaseName("IX_DoctorReviews_DoctorId_IsDemo_ModerationStatus");

        builder.HasIndex(r => new { r.PatientId, r.DoctorId })
            .HasDatabaseName("IX_DoctorReviews_PatientId_DoctorId");
    }
}
