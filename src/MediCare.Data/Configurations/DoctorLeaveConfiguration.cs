using MediCare.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediCare.Data.Configurations;

public class DoctorLeaveConfiguration : IEntityTypeConfiguration<DoctorLeave>
{
    public void Configure(EntityTypeBuilder<DoctorLeave> builder)
    {
        builder.ToTable("DoctorLeaves");

        builder.HasKey(l => l.Id);

        builder.Property(l => l.StartDate)
            .HasColumnType("date")
            .IsRequired();

        builder.Property(l => l.EndDate)
            .HasColumnType("date")
            .IsRequired();

        builder.Property(l => l.Reason)
            .HasMaxLength(250);

        builder.Property(l => l.CreatedAt)
            .HasDefaultValueSql("GETUTCDATE()");

        builder.HasIndex(l => new { l.DoctorId, l.StartDate, l.EndDate })
            .HasDatabaseName("IX_DoctorLeaves_Doctor_Dates");

        builder.HasOne(l => l.Doctor)
            .WithMany(d => d.Leaves)
            .HasForeignKey(l => l.DoctorId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
