using System;
using System.Collections.Generic;
using MediCare.Data.Enums;
using MediCare.Services.Common;

namespace MediCare.Services.DTOs;

public class DoctorReviewDto
{
    public int Id { get; set; }
    public int DoctorId { get; set; }
    public string DoctorName { get; set; } = string.Empty;
    public string DoctorSpecialization { get; set; } = string.Empty;
    public int? PatientId { get; set; }
    public int? AppointmentId { get; set; }

    public int Rating { get; set; } = 5;
    public string? Title { get; set; }
    public string Comment { get; set; } = string.Empty;
    public string PatientDisplayName { get; set; } = string.Empty;

    // Backward-compatibility alias for previous DoctorReviewDto
    public string PatientName
    {
        get => PatientDisplayName;
        set => PatientDisplayName = value;
    }

    public string PatientInitials
    {
        get
        {
            if (string.IsNullOrWhiteSpace(PatientDisplayName)) return "PT";
            var parts = PatientDisplayName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 1) return parts[0].Length >= 2 ? parts[0][..2].ToUpperInvariant() : parts[0].ToUpperInvariant();
            return $"{parts[0][0]}{parts[^1][0]}".ToUpperInvariant();
        }
    }

    public bool IsVerifiedAppointment { get; set; }

    // Backward-compatibility alias
    public bool IsVerifiedVisit
    {
        get => IsVerifiedAppointment;
        set => IsVerifiedAppointment = value;
    }

    public bool IsDemo { get; set; }
    public ReviewModerationStatus ModerationStatus { get; set; } = ReviewModerationStatus.Approved;
    public int HelpfulCount { get; set; }
    public bool HasUserVotedHelpful { get; set; }

    // Doctor Response
    public string? DoctorResponse { get; set; }
    public DateTime? DoctorResponseDateUtc { get; set; }

    // Admin Moderation Audit
    public string? ModeratedByAdminId { get; set; }
    public DateTime? ModeratedAtUtc { get; set; }
    public string? ModerationReason { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    // Formatted presentation properties
    public string FormattedDate => CreatedAt.ToString("MMM dd, yyyy");

    public string TimeAgo
    {
        get
        {
            var span = DateTime.UtcNow - CreatedAt;
            if (span.TotalDays >= 365)
            {
                var years = (int)(span.TotalDays / 365);
                return years == 1 ? "1 year ago" : $"{years} years ago";
            }
            if (span.TotalDays >= 30)
            {
                var months = (int)(span.TotalDays / 30);
                return months == 1 ? "1 month ago" : $"{months} months ago";
            }
            if (span.TotalDays >= 1)
            {
                var days = (int)span.TotalDays;
                return days == 1 ? "Yesterday" : $"{days} days ago";
            }
            if (span.TotalHours >= 1)
            {
                var hours = (int)span.TotalHours;
                return hours == 1 ? "1 hour ago" : $"{hours} hours ago";
            }
            if (span.TotalMinutes >= 1)
            {
                var mins = (int)span.TotalMinutes;
                return mins == 1 ? "1 minute ago" : $"{mins} minutes ago";
            }
            return "Just now";
        }
    }
}

public class DoctorRatingSummaryDto
{
    public int DoctorId { get; set; }
    public string DoctorName { get; set; } = string.Empty;
    public double AverageRating { get; set; }
    public int TotalReviewsCount { get; set; }
    public int VerifiedReviewsCount { get; set; }

    public int FiveStarCount { get; set; }
    public int FourStarCount { get; set; }
    public int ThreeStarCount { get; set; }
    public int TwoStarCount { get; set; }
    public int OneStarCount { get; set; }

    public int FiveStarPercentage => TotalReviewsCount == 0 ? 0 : (int)Math.Round((double)FiveStarCount / TotalReviewsCount * 100);
    public int FourStarPercentage => TotalReviewsCount == 0 ? 0 : (int)Math.Round((double)FourStarCount / TotalReviewsCount * 100);
    public int ThreeStarPercentage => TotalReviewsCount == 0 ? 0 : (int)Math.Round((double)ThreeStarCount / TotalReviewsCount * 100);
    public int TwoStarPercentage => TotalReviewsCount == 0 ? 0 : (int)Math.Round((double)TwoStarCount / TotalReviewsCount * 100);
    public int OneStarPercentage => TotalReviewsCount == 0 ? 0 : (int)Math.Round((double)OneStarCount / TotalReviewsCount * 100);
}

public class DoctorReviewsQueryDto
{
    public int DoctorId { get; set; }
    public int? RatingFilter { get; set; }
    public string SortBy { get; set; } = "recent"; // "recent", "highest", "lowest", "helpful"
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 10;
    public string? CurrentUserId { get; set; }
}

public class DoctorReviewsHubDto
{
    public DoctorRatingSummaryDto Summary { get; set; } = new();
    public PagedResult<DoctorReviewDto> Reviews { get; set; } = new();
    public List<EligibleAppointmentForReviewDto> EligibleAppointments { get; set; } = new();
    public bool CanSubmitReview => EligibleAppointments.Count > 0;
    public bool IsAuthenticated { get; set; }
    public bool IsPatient { get; set; }
    public int? SelectedRatingFilter { get; set; }
    public string CurrentSortBy { get; set; } = "recent";
}

public class SubmitDoctorReviewDto
{
    public int DoctorId { get; set; }
    public int AppointmentId { get; set; }
    public int Rating { get; set; }
    public string? Title { get; set; }
    public string Comment { get; set; } = string.Empty;
    public string? PatientDisplayName { get; set; }
}

public class DoctorReplyReviewDto
{
    public int ReviewId { get; set; }
    public string Response { get; set; } = string.Empty;
}

public class AdminModerateReviewDto
{
    public int ReviewId { get; set; }
    public ReviewModerationStatus NewStatus { get; set; }
    public string? Reason { get; set; }
}

public class EligibleAppointmentForReviewDto
{
    public int AppointmentId { get; set; }
    public int DoctorId { get; set; }
    public string DoctorName { get; set; } = string.Empty;
    public DateTime AppointmentDate { get; set; }
    public string FormattedAppointmentDate => AppointmentDate.ToString("dddd, MMM dd, yyyy - hh:mm tt");
}

public class ReviewHelpfulVoteResultDto
{
    public bool Success { get; set; }
    public int ReviewId { get; set; }
    public int HelpfulCount { get; set; }
    public bool HasVoted { get; set; }
    public string Message { get; set; } = string.Empty;
}
