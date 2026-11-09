using MediCare.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediCare.Data.Configurations;

public class PatientConfiguration : IEntityTypeConfiguration<Patient>
{
    public void Configure(EntityTypeBuilder<Patient> builder)
    {
        builder.ToTable("Patients");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.UserId)
            .IsRequired()
            .HasMaxLength(450);

        builder.HasIndex(p => p.UserId)
            .IsUnique()
            .HasDatabaseName("IX_Patients_UserId");

        builder.Property(p => p.DateOfBirth)
            .HasColumnType("date")
            .IsRequired();

        builder.Property(p => p.Gender)
            .IsRequired()
            .HasMaxLength(10);

        builder.Property(p => p.BloodGroup)
            .HasMaxLength(5);

        builder.Property(p => p.EmergencyContact)
            .HasMaxLength(50);

        builder.Property(p => p.Allergies)
            .HasMaxLength(500);

        builder.Property(p => p.MedicalHistory)
            .HasMaxLength(1000);

        builder.Property(p => p.CreatedAt)
            .HasDefaultValueSql("GETUTCDATE()");

        builder.HasOne(p => p.User)
            .WithOne(u => u.Patient)
            .HasForeignKey<Patient>(p => p.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(p => p.User).AutoInclude();
    }
}
