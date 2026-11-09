using MediCare.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediCare.Data.Configurations;

public class RefundConfiguration : IEntityTypeConfiguration<Refund>
{
    public void Configure(EntityTypeBuilder<Refund> builder)
    {
        builder.ToTable("Refunds");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.AuditReference).HasMaxLength(100).IsRequired();
        builder.Property(x => x.ProviderTransactionId).HasMaxLength(100).IsRequired();
        builder.Property(x => x.ProviderRefundId).HasMaxLength(100);
        builder.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(30).IsRequired();
        builder.Property(x => x.Reason).HasMaxLength(500).IsRequired();
        builder.Property(x => x.RequestedByAdminId).HasMaxLength(100).IsRequired();
        builder.Property(x => x.ApprovedByAdminId).HasMaxLength(100);
        builder.Property(x => x.FailureReason).HasMaxLength(500);

        builder.HasIndex(x => x.AuditReference).IsUnique();
        builder.HasIndex(x => x.PaymentAttemptId);
        builder.HasIndex(x => new { x.PaymentAttemptId, x.Status });
        builder.HasIndex(x => x.ProviderRefundId).HasFilter("[ProviderRefundId] IS NOT NULL");
        builder.HasIndex(x => x.Status);

        builder.HasOne(x => x.PaymentAttempt)
            .WithMany(p => p.Refunds)
            .HasForeignKey(x => x.PaymentAttemptId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
