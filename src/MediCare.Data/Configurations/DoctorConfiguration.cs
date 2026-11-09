using MediCare.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediCare.Data.Configurations;

public class DoctorConfiguration : IEntityTypeConfiguration<Doctor>
{
    public void Configure(EntityTypeBuilder<Doctor> builder)
    {
        builder.ToTable("Doctors");

        builder.HasKey(d => d.Id);

        builder.Property(d => d.UserId)
            .IsRequired()
            .HasMaxLength(450);

        builder.HasIndex(d => d.UserId)
            .IsUnique()
            .HasDatabaseName("IX_Doctors_UserId");

        builder.Property(d => d.LicenseNumber)
            .IsRequired()
            .HasMaxLength(50);

        builder.HasIndex(d => d.LicenseNumber)
            .IsUnique()
            .HasDatabaseName("IX_Doctors_LicenseNumber");

        builder.Property(d => d.ConsultationFee)
            .HasColumnType("decimal(18,2)")
            .IsRequired();

        builder.Property(d => d.SlotDurationMinutes)
            .HasDefaultValue(30);

        builder.Property(d => d.IsApproved)
            .HasDefaultValue(false);

        builder.Property(d => d.Governorate)
            .IsRequired()
            .HasMaxLength(100)
            .HasDefaultValue("Cairo");

        builder.Property(d => d.ProfileImageUrl)
            .HasMaxLength(500);

        builder.Property(d => d.Bio)
            .HasMaxLength(1000);

        builder.Property(d => d.CreatedAt)
            .HasDefaultValueSql("GETUTCDATE()");

        builder.HasOne(d => d.User)
            .WithOne(u => u.Doctor)
            .HasForeignKey<Doctor>(d => d.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(d => d.Specialization)
            .WithMany(s => s.Doctors)
            .HasForeignKey(d => d.SpecializationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(d => d.SpecializationId)
            .HasDatabaseName("IX_Doctors_SpecializationId");

        // PERF-07: Filtered index for approved doctor directory searches
        builder.HasIndex(d => new { d.SpecializationId, d.Governorate })
            .HasFilter("[IsApproved] = 1")
            .HasDatabaseName("IX_Doctors_Approved_Search")
            .IncludeProperties(d => new { d.ConsultationFee, d.SlotDurationMinutes });
    }
}
