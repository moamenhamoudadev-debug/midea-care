using System.Collections.Generic;
using MediCare.Data.Enums;
using MediCare.Services.Common;
using MediCare.Services.DTOs;

namespace MediCare.Web.ViewModels;

public class DoctorDetailsWithReviewsViewModel
{
    public DoctorDetailDto Doctor { get; set; } = new();
    public DoctorReviewsHubDto ReviewsHub { get; set; } = new();
}

public class DoctorPortalReviewsViewModel
{
    public DoctorDetailDto Doctor { get; set; } = new();
    public DoctorRatingSummaryDto Summary { get; set; } = new();
    public PagedResult<DoctorReviewDto> Reviews { get; set; } = new();
    public int? RatingFilter { get; set; }
    public string SortBy { get; set; } = "recent";
}

public class AdminReviewsViewModel
{
    public PagedResult<DoctorReviewDto> Reviews { get; set; } = new();
    public ReviewModerationStatus? StatusFilter { get; set; }
    public int? DoctorIdFilter { get; set; }
    public int TotalApprovedReviews { get; set; }
    public int TotalHiddenReviews { get; set; }
    public int TotalDemoReviews { get; set; }
    public int TotalVerifiedReviews { get; set; }
}
