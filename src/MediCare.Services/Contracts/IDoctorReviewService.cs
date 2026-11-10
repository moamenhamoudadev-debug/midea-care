using System.Collections.Generic;
using System.Threading.Tasks;
using MediCare.Data.Enums;
using MediCare.Services.Common;
using MediCare.Services.DTOs;

namespace MediCare.Services.Contracts;

public interface IDoctorReviewService
{
    Task<DoctorRatingSummaryDto> GetDoctorRatingSummaryAsync(int doctorId);
    Task<PagedResult<DoctorReviewDto>> GetDoctorReviewsAsync(DoctorReviewsQueryDto query);
    Task<DoctorReviewsHubDto> GetDoctorReviewsHubAsync(int doctorId, string? currentUserId, int? ratingFilter = null, string sortBy = "recent", int pageNumber = 1, int pageSize = 10);
    Task<List<EligibleAppointmentForReviewDto>> GetEligibleAppointmentsForReviewAsync(string userId, int doctorId);
    Task<Result<DoctorReviewDto>> SubmitReviewAsync(SubmitDoctorReviewDto dto, string currentUserId);
    Task<Result<ReviewHelpfulVoteResultDto>> ToggleHelpfulVoteAsync(int reviewId, string currentUserId, string? ipAddress = null);
    Task<Result<DoctorReviewDto>> DoctorReplyAsync(DoctorReplyReviewDto dto, string doctorUserId);
    Task<Result<DoctorReviewDto>> AdminModerateReviewAsync(AdminModerateReviewDto dto, string adminUserId);
    Task<PagedResult<DoctorReviewDto>> GetAdminReviewQueueAsync(ReviewModerationStatus? status = null, int? doctorId = null, int pageNumber = 1, int pageSize = 20);
    Task<int> SeedDemoReviewsAsync(bool force = false);
    Task<int> PurgeDemoReviewsAsync();
}
