using MediCare.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediCare.Data.Configurations;

public class NotificationOutboxConfiguration : IEntityTypeConfiguration<NotificationOutbox>
{
    public void Configure(EntityTypeBuilder<NotificationOutbox> builder)
    {
        builder.ToTable("NotificationOutbox");

        builder.HasKey(o => o.Id);

        builder.Property(o => o.UserId)
            .IsRequired()
            .HasMaxLength(450);

        builder.Property(o => o.Channel)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(o => o.RecipientEmail)
            .HasMaxLength(256);

        builder.Property(o => o.Subject)
            .HasMaxLength(200);

        builder.Property(o => o.Status)
            .IsRequired()
            .HasMaxLength(30)
            .HasDefaultValue("Pending");

        builder.Property(o => o.RetryCount)
            .HasDefaultValue(0);

        builder.Property(o => o.MaxRetries)
            .HasDefaultValue(3);

        builder.Property(o => o.LastError)
            .HasMaxLength(1000);

        builder.Property(o => o.CreatedAt)
            .HasDefaultValueSql("GETUTCDATE()");

        builder.HasIndex(o => o.Status)
            .HasDatabaseName("IX_NotificationOutbox_Status");

        builder.HasIndex(o => new { o.Status, o.CreatedAt })
            .HasDatabaseName("IX_NotificationOutbox_Status_CreatedAt");

        builder.HasOne(o => o.Notification)
            .WithMany()
            .HasForeignKey(o => o.NotificationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
