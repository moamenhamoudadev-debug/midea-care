using MediCare.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediCare.Data.Configurations;

public class MedicalRecordConfiguration : IEntityTypeConfiguration<MedicalRecord>
{
    public void Configure(EntityTypeBuilder<MedicalRecord> builder)
    {
        builder.ToTable("MedicalRecords");

        builder.HasKey(m => m.Id);

        builder.Property(m => m.Diagnosis)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(m => m.Symptoms)
            .HasMaxLength(1000);

        builder.Property(m => m.AttachmentPath)
            .HasMaxLength(500);

        builder.Property(m => m.IsDraft)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(m => m.CreatedAt)
            .HasDefaultValueSql("GETUTCDATE()");

        builder.HasIndex(m => m.AppointmentId)
            .IsUnique()
            .HasDatabaseName("IX_MedicalRecords_AppointmentId");

        builder.HasOne(m => m.Appointment)
            .WithOne(a => a.MedicalRecord)
            .HasForeignKey<MedicalRecord>(m => m.AppointmentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(m => m.Doctor)
            .WithMany(d => d.MedicalRecords)
            .HasForeignKey(m => m.DoctorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(m => m.Patient)
            .WithMany(p => p.MedicalRecords)
            .HasForeignKey(m => m.PatientId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
