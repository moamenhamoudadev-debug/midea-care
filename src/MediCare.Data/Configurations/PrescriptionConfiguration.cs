using MediCare.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediCare.Data.Configurations;

public class PrescriptionConfiguration : IEntityTypeConfiguration<Prescription>
{
    public void Configure(EntityTypeBuilder<Prescription> builder)
    {
        builder.ToTable("Prescriptions");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.PrescriptionDate)
            .HasDefaultValueSql("GETUTCDATE()");

        builder.Property(p => p.Notes)
            .HasMaxLength(500);

        builder.Property(p => p.VerificationToken)
            .IsRequired()
            .HasMaxLength(64);

        builder.HasIndex(p => p.VerificationToken)
            .IsUnique()
            .HasDatabaseName("IX_Prescriptions_VerificationToken");

        builder.Property(p => p.IsDispensed)
            .IsConcurrencyToken()
            .HasDefaultValue(false);

        builder.Property(p => p.DispensedByUserId)
            .HasMaxLength(450);

        builder.Property(p => p.PharmacyNotes)
            .HasMaxLength(500);

        builder.Property(p => p.CreatedAt)
            .HasDefaultValueSql("GETUTCDATE()");

        builder.HasIndex(p => p.MedicalRecordId)
            .IsUnique()
            .HasDatabaseName("IX_Prescriptions_MedicalRecordId");

        builder.HasOne(p => p.MedicalRecord)
            .WithOne(m => m.Prescription)
            .HasForeignKey<Prescription>(p => p.MedicalRecordId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(p => p.Doctor)
            .WithMany(d => d.Prescriptions)
            .HasForeignKey(p => p.DoctorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(p => p.Patient)
            .WithMany(p => p.Prescriptions)
            .HasForeignKey(p => p.PatientId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(p => p.DispensedByUser)
            .WithMany()
            .HasForeignKey(p => p.DispensedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
