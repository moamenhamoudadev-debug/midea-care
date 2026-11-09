using MediCare.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediCare.Data.Configurations;

public class PaymentAttemptConfiguration : IEntityTypeConfiguration<PaymentAttempt>
{
    public void Configure(EntityTypeBuilder<PaymentAttempt> builder)
    {
        builder.ToTable("PaymentAttempts");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.MerchantReference).HasMaxLength(100).IsRequired();
        builder.Property(x => x.ProviderOrderId).HasMaxLength(100);
        builder.Property(x => x.ProviderTransactionId).HasMaxLength(100);
        builder.Property(x => x.CheckoutUrl).HasMaxLength(2000);
        builder.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(20).IsRequired();
        builder.Property(x => x.PromoCode).HasMaxLength(50);
        builder.Property(x => x.TermsVersion).HasMaxLength(50);
        builder.Property(x => x.TermsAcceptedAt);
        builder.Property(x => x.ReconciliationStatus).HasMaxLength(50);
        builder.Property(x => x.ReconciliationReason).HasMaxLength(255);
        builder.Property(x => x.PaymentMethodType).HasMaxLength(50);
        builder.Property(x => x.PaymentMethodDetail).HasMaxLength(100);
        builder.Property(x => x.DiscountAmount).HasColumnType("decimal(18,2)");
        builder.HasIndex(x => x.MerchantReference).IsUnique();
        builder.HasIndex(x => x.ProviderTransactionId).IsUnique().HasFilter("[ProviderTransactionId] IS NOT NULL");
        builder.HasIndex(x => new { x.AppointmentId, x.Status });
        builder.HasIndex(x => x.AppointmentId).IsUnique().HasFilter("[Status] = N'Pending'").HasDatabaseName("IX_PaymentAttempts_OnePendingPerAppointment");
        builder.HasIndex(x => new { x.PatientId, x.PromoCode })
            .IsUnique()
            .HasFilter("[PromoCode] IS NOT NULL AND [Status] IN (N'Pending', N'Succeeded')")
            .HasDatabaseName("IX_PaymentAttempts_PatientPromo_ActiveOrSucceeded");
        builder.HasOne(x => x.Appointment).WithMany().HasForeignKey(x => x.AppointmentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Patient>().WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
    }
}
