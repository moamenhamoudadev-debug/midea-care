using MediCare.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediCare.Data.Configurations;

public class AppointmentConfiguration : IEntityTypeConfiguration<Appointment>
{
    public void Configure(EntityTypeBuilder<Appointment> builder)
    {
        builder.ToTable("Appointments");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.AppointmentDate)
            .HasColumnType("date")
            .IsRequired();

        builder.Property(a => a.StartTime)
            .HasColumnType("time(0)")
            .IsRequired();

        builder.Property(a => a.EndTime)
            .HasColumnType("time(0)")
            .IsRequired();

        builder.Property(a => a.Status)
            .IsConcurrencyToken()
            .IsRequired();

        builder.Property(a => a.ConsultationFee)
            .HasColumnType("decimal(18,2)")
            .IsRequired();

        builder.Property(a => a.PaymentStatus)
            .IsConcurrencyToken()
            .IsRequired();

        builder.Property(a => a.PaidAmount)
            .HasColumnType("decimal(18,2)");

        builder.Property(a => a.DiscountAmount)
            .HasColumnType("decimal(18,2)");

        builder.Property(a => a.PromoCode)
            .HasMaxLength(50);

        builder.Property(a => a.TransactionReference)
            .HasMaxLength(100);

        builder.Property(a => a.PaymentProvider).HasMaxLength(50);

        builder.Property(a => a.PaidAt)
            .HasColumnType("datetime2");

        builder.Property(a => a.CheckedInAt)
            .HasColumnType("datetime2");

        builder.HasIndex(a => a.TransactionReference)
            .IsUnique()
            .HasFilter("[TransactionReference] IS NOT NULL")
            .HasDatabaseName("IX_Appointments_TransactionReference");

        builder.Property(a => a.Type)
            .IsRequired();

        builder.Property(a => a.ReminderSent)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(a => a.Notes)
            .HasMaxLength(500);

        builder.Property(a => a.TelehealthRoomSecret)
            .HasMaxLength(64);

        builder.Property(a => a.CreatedAt)
            .HasDefaultValueSql("GETUTCDATE()");

        // Concurrency-Safe Filtered Unique Index: guarantees 0 double-booking for active slots
        // Note: SQL Server filtered indexes do not support NOT / NOT IN predicates; <> and AND are required.
        builder.HasIndex(a => new { a.DoctorId, a.AppointmentDate, a.StartTime })
            .IsUnique()
            .HasFilter("[Status] <> 3 AND [Status] <> 4")
            .HasDatabaseName("IX_Appointments_Doctor_NoOverlap");

        // BIZ-01: Filtered unique index prevents a patient from reusing a promo code across multiple paid appointments
        builder.HasIndex(a => new { a.PatientId, a.PromoCode })
            .IsUnique()
            .HasFilter("[PromoCode] IS NOT NULL AND [PaymentStatus] = 1")
            .HasDatabaseName("IX_Appointments_Patient_PromoCode_Paid");

        // PERF-07: Covering composite index for Doctor Appointment queries (all statuses including Cancelled/Rejected)
        builder.HasIndex(a => new { a.DoctorId, a.Status, a.AppointmentDate, a.StartTime })
            .HasDatabaseName("IX_Appointments_Doctor_Status_Date")
            .IsDescending(false, false, true, true)
            .IncludeProperties(a => new { a.PaymentStatus, a.ConsultationFee, a.Type, a.CheckedInAt });

        // PERF-07: Covering index for Patient Appointment history
        builder.HasIndex(a => new { a.PatientId, a.AppointmentDate, a.StartTime })
            .HasDatabaseName("IX_Appointments_Patient_AppointmentDate")
            .IsDescending(false, true, true)
            .IncludeProperties(a => new { a.Status, a.PaymentStatus, a.ConsultationFee, a.Type });

        // PERF-07: Filtered index for 24-hour reminder background job
        builder.HasIndex(a => new { a.AppointmentDate, a.StartTime })
            .HasFilter("[ReminderSent] = 0")
            .HasDatabaseName("IX_Appointments_PendingReminders")
            .IncludeProperties(a => new { a.DoctorId, a.PatientId, a.Status });

        builder.HasOne(a => a.Doctor)
            .WithMany(d => d.Appointments)
            .HasForeignKey(a => a.DoctorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(a => a.Patient)
            .WithMany(p => p.Appointments)
            .HasForeignKey(a => a.PatientId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
