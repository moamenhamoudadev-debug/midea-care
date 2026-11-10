using MediCare.Data.Enums;

namespace MediCare.Data.Entities;

public class DoctorReview : BaseAuditableEntity
{
    public int DoctorId { get; set; }
    public int? PatientId { get; set; }
    public int? AppointmentId { get; set; }

    public int Rating { get; set; }
    public string? Title { get; set; }
    public string Comment { get; set; } = string.Empty;

    public string PatientDisplayName { get; set; } = string.Empty;
    public bool IsVerifiedAppointment { get; set; }
    public bool IsDemo { get; set; }

    public ReviewModerationStatus ModerationStatus { get; set; } = ReviewModerationStatus.Approved;
    public int HelpfulCount { get; set; } = 0;

    // Doctor professional response
    public string? DoctorResponse { get; set; }
    public DateTime? DoctorResponseDateUtc { get; set; }

    // Admin moderation audit trail
    public string? ModeratedByAdminId { get; set; }
    public DateTime? ModeratedAtUtc { get; set; }
    public string? ModerationReason { get; set; }

    // Navigation properties
    public virtual Doctor Doctor { get; set; } = null!;
    public virtual Patient? Patient { get; set; }
    public virtual Appointment? Appointment { get; set; }
    public virtual ApplicationUser? ModeratedByAdmin { get; set; }
    public virtual ICollection<DoctorReviewHelpfulVote> HelpfulVotes { get; set; } = new List<DoctorReviewHelpfulVote>();
}
