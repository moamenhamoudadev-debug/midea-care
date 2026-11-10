using MediCare.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediCare.Data.Configurations;

public class DoctorReviewHelpfulVoteConfiguration : IEntityTypeConfiguration<DoctorReviewHelpfulVote>
{
    public void Configure(EntityTypeBuilder<DoctorReviewHelpfulVote> builder)
    {
        builder.ToTable("DoctorReviewHelpfulVotes");

        builder.HasKey(v => v.Id);

        builder.Property(v => v.ReviewId)
            .IsRequired();

        builder.Property(v => v.UserId)
            .IsRequired()
            .HasMaxLength(450);

        builder.Property(v => v.UserIpHash)
            .HasMaxLength(128);

        builder.Property(v => v.CreatedAtUtc)
            .IsRequired();

        // Relationships
        builder.HasOne(v => v.Review)
            .WithMany(r => r.HelpfulVotes)
            .HasForeignKey(v => v.ReviewId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(v => v.User)
            .WithMany()
            .HasForeignKey(v => v.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Unique constraint on ReviewId + UserId
        builder.HasIndex(v => new { v.ReviewId, v.UserId })
            .IsUnique()
            .HasDatabaseName("IX_DoctorReviewHelpfulVotes_ReviewId_UserId");
    }
}
