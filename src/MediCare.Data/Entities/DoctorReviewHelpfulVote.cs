namespace MediCare.Data.Entities;

public class DoctorReviewHelpfulVote
{
    public int Id { get; set; }
    public int ReviewId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string? UserIpHash { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    public virtual DoctorReview Review { get; set; } = null!;
    public virtual ApplicationUser User { get; set; } = null!;
}
