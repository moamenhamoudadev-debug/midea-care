using MediCare.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediCare.Data.Configurations;

public class WorkingHoursConfiguration : IEntityTypeConfiguration<WorkingHours>
{
    public void Configure(EntityTypeBuilder<WorkingHours> builder)
    {
        builder.ToTable("WorkingHours");

        builder.HasKey(w => w.Id);

        builder.Property(w => w.DayOfWeek)
            .IsRequired();

        builder.Property(w => w.StartTime)
            .HasColumnType("time(0)")
            .IsRequired();

        builder.Property(w => w.EndTime)
            .HasColumnType("time(0)")
            .IsRequired();

        builder.Property(w => w.CreatedAt)
            .HasDefaultValueSql("GETUTCDATE()");

        builder.HasIndex(w => new { w.DoctorId, w.DayOfWeek })
            .HasDatabaseName("IX_WorkingHours_Doctor_Day");

        builder.HasOne(w => w.Doctor)
            .WithMany(d => d.WorkingHours)
            .HasForeignKey(w => w.DoctorId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
