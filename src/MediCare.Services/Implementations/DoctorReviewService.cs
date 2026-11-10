using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using FluentValidation;
using MediCare.Data.Context;
using MediCare.Data.Entities;
using MediCare.Data.Enums;
using MediCare.Services.Common;
using MediCare.Services.Contracts;
using MediCare.Services.DTOs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MediCare.Services.Implementations;

public class DoctorReviewService : IDoctorReviewService
{
    private readonly ApplicationDbContext _context;
    private readonly IHostEnvironment? _environment;
    private readonly IConfiguration? _configuration;
    private readonly IValidator<SubmitDoctorReviewDto>? _validator;
    private readonly ILogger<DoctorReviewService> _logger;

    public DoctorReviewService(
        ApplicationDbContext context,
        ILogger<DoctorReviewService> logger,
        IHostEnvironment? environment = null,
        IConfiguration? configuration = null,
        IValidator<SubmitDoctorReviewDto>? validator = null)
    {
        _context = context;
        _logger = logger;
        _environment = environment;
        _configuration = configuration;
        _validator = validator;
    }

    private bool IsDemoModeAllowed()
    {
        if (_environment?.IsDevelopment() == true) return true;
        var configVal = _configuration?["Seed:EnableDemoReviews"];
        return string.Equals(configVal, "true", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<DoctorRatingSummaryDto> GetDoctorRatingSummaryAsync(int doctorId)
    {
        var doctorName = await _context.Doctors
            .AsNoTracking()
            .Where(d => d.Id == doctorId)
            .Select(d => d.User.FullName)
            .FirstOrDefaultAsync() ?? "Doctor";

        var baseQuery = _context.DoctorReviews
            .AsNoTracking()
            .Where(r => r.DoctorId == doctorId && r.ModerationStatus == ReviewModerationStatus.Approved);

        // Strict isolation: exclude demo reviews from production rating aggregation
        if (!IsDemoModeAllowed())
        {
            baseQuery = baseQuery.Where(r => !r.IsDemo);
        }

        var stats = await baseQuery
            .GroupBy(r => 1)
            .Select(g => new
            {
                Total = g.Count(),
                Avg = g.Average(r => (double)r.Rating),
                Verified = g.Count(r => r.IsVerifiedAppointment),
                Five = g.Count(r => r.Rating == 5),
                Four = g.Count(r => r.Rating == 4),
                Three = g.Count(r => r.Rating == 3),
                Two = g.Count(r => r.Rating == 2),
                One = g.Count(r => r.Rating == 1)
            })
            .FirstOrDefaultAsync();

        if (stats == null || stats.Total == 0)
        {
            return new DoctorRatingSummaryDto
            {
                DoctorId = doctorId,
                DoctorName = doctorName,
                AverageRating = 0.0,
                TotalReviewsCount = 0,
                VerifiedReviewsCount = 0,
                FiveStarCount = 0,
                FourStarCount = 0,
                ThreeStarCount = 0,
                TwoStarCount = 0,
                OneStarCount = 0
            };
        }

        return new DoctorRatingSummaryDto
        {
            DoctorId = doctorId,
            DoctorName = doctorName,
            AverageRating = Math.Round(stats.Avg, 1),
            TotalReviewsCount = stats.Total,
            VerifiedReviewsCount = stats.Verified,
            FiveStarCount = stats.Five,
            FourStarCount = stats.Four,
            ThreeStarCount = stats.Three,
            TwoStarCount = stats.Two,
            OneStarCount = stats.One
        };
    }

    public async Task<PagedResult<DoctorReviewDto>> GetDoctorReviewsAsync(DoctorReviewsQueryDto query)
    {
        var baseQuery = _context.DoctorReviews
            .AsNoTracking()
            .Include(r => r.Doctor).ThenInclude(d => d.User)
            .Include(r => r.Doctor).ThenInclude(d => d.Specialization)
            .Where(r => r.DoctorId == query.DoctorId && r.ModerationStatus == ReviewModerationStatus.Approved);

        if (!IsDemoModeAllowed())
        {
            baseQuery = baseQuery.Where(r => !r.IsDemo);
        }

        if (query.RatingFilter.HasValue && query.RatingFilter.Value >= 1 && query.RatingFilter.Value <= 5)
        {
            baseQuery = baseQuery.Where(r => r.Rating == query.RatingFilter.Value);
        }

        baseQuery = (query.SortBy?.ToLowerInvariant()) switch
        {
            "highest" => baseQuery.OrderByDescending(r => r.Rating).ThenByDescending(r => r.CreatedAt),
            "lowest" => baseQuery.OrderBy(r => r.Rating).ThenByDescending(r => r.CreatedAt),
            "helpful" => baseQuery.OrderByDescending(r => r.HelpfulCount).ThenByDescending(r => r.CreatedAt),
            _ => baseQuery.OrderByDescending(r => r.CreatedAt)
        };

        var totalCount = await baseQuery.CountAsync();
        var pageNumber = query.PageNumber < 1 ? 1 : query.PageNumber;
        var pageSize = query.PageSize < 1 ? 10 : query.PageSize;

        var items = await baseQuery
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var reviewIds = items.Select(i => i.Id).ToList();
        var userVotedReviewIds = new HashSet<int>();

        if (!string.IsNullOrEmpty(query.CurrentUserId) && reviewIds.Count > 0)
        {
            userVotedReviewIds = new HashSet<int>(await _context.DoctorReviewHelpfulVotes
                .AsNoTracking()
                .Where(v => v.UserId == query.CurrentUserId && reviewIds.Contains(v.ReviewId))
                .Select(v => v.ReviewId)
                .ToListAsync());
        }

        var dtos = items.Select(r => MapToDto(r, userVotedReviewIds.Contains(r.Id))).ToList();
        return new PagedResult<DoctorReviewDto>(dtos, totalCount, pageNumber, pageSize);
    }

    public async Task<DoctorReviewsHubDto> GetDoctorReviewsHubAsync(
        int doctorId,
        string? currentUserId,
        int? ratingFilter = null,
        string sortBy = "recent",
        int pageNumber = 1,
        int pageSize = 10)
    {
        var summary = await GetDoctorRatingSummaryAsync(doctorId);
        var reviews = await GetDoctorReviewsAsync(new DoctorReviewsQueryDto
        {
            DoctorId = doctorId,
            RatingFilter = ratingFilter,
            SortBy = sortBy,
            PageNumber = pageNumber,
            PageSize = pageSize,
            CurrentUserId = currentUserId
        });

        var eligibleAppointments = new List<EligibleAppointmentForReviewDto>();
        bool isPatient = false;

        if (!string.IsNullOrEmpty(currentUserId))
        {
            var patient = await _context.Patients
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.UserId == currentUserId);

            if (patient != null)
            {
                isPatient = true;
                eligibleAppointments = await GetEligibleAppointmentsForReviewAsync(currentUserId, doctorId);
            }
        }

        return new DoctorReviewsHubDto
        {
            Summary = summary,
            Reviews = reviews,
            EligibleAppointments = eligibleAppointments,
            IsAuthenticated = !string.IsNullOrEmpty(currentUserId),
            IsPatient = isPatient,
            SelectedRatingFilter = ratingFilter,
            CurrentSortBy = string.IsNullOrWhiteSpace(sortBy) ? "recent" : sortBy
        };
    }

    public async Task<List<EligibleAppointmentForReviewDto>> GetEligibleAppointmentsForReviewAsync(string userId, int doctorId)
    {
        var patient = await _context.Patients
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId);

        if (patient == null) return new List<EligibleAppointmentForReviewDto>();

        return await _context.Appointments
            .AsNoTracking()
            .Include(a => a.Doctor).ThenInclude(d => d.User)
            .Where(a => a.PatientId == patient.Id
                     && a.DoctorId == doctorId
                     && a.Status == AppointmentStatus.Completed
                     && !_context.DoctorReviews.Any(r => r.AppointmentId == a.Id))
            .OrderByDescending(a => a.AppointmentDate)
            .Select(a => new EligibleAppointmentForReviewDto
            {
                AppointmentId = a.Id,
                DoctorId = a.DoctorId,
                DoctorName = a.Doctor.User.FullName,
                AppointmentDate = a.AppointmentDate
            })
            .ToListAsync();
    }

    public async Task<Result<DoctorReviewDto>> SubmitReviewAsync(SubmitDoctorReviewDto dto, string currentUserId)
    {
        if (_validator != null)
        {
            var valResult = await _validator.ValidateAsync(dto);
            if (!valResult.IsValid)
            {
                return Result<DoctorReviewDto>.Failure(valResult.Errors.First().ErrorMessage);
            }
        }

        var patient = await _context.Patients
            .Include(p => p.User)
            .FirstOrDefaultAsync(p => p.UserId == currentUserId);

        if (patient == null)
        {
            return Result<DoctorReviewDto>.Failure("Only registered patients can submit reviews.");
        }

        var appointment = await _context.Appointments
            .Include(a => a.Doctor).ThenInclude(d => d.User)
            .FirstOrDefaultAsync(a => a.Id == dto.AppointmentId);

        if (appointment == null)
        {
            return Result<DoctorReviewDto>.Failure("Appointment record not found.");
        }

        if (appointment.PatientId != patient.Id)
        {
            return Result<DoctorReviewDto>.Failure("You can only review appointments that belong to your account.");
        }

        if (appointment.DoctorId != dto.DoctorId)
        {
            return Result<DoctorReviewDto>.Failure("Appointment does not match the specified doctor.");
        }

        if (appointment.Status != AppointmentStatus.Completed)
        {
            return Result<DoctorReviewDto>.Failure("Reviews can only be submitted for completed appointments.");
        }

        var alreadyReviewed = await _context.DoctorReviews.AnyAsync(r => r.AppointmentId == dto.AppointmentId);
        if (alreadyReviewed)
        {
            return Result<DoctorReviewDto>.Failure("A review has already been submitted for this appointment.");
        }

        // Determine privacy-preserving display name
        string displayName;
        if (!string.IsNullOrWhiteSpace(dto.PatientDisplayName))
        {
            displayName = dto.PatientDisplayName.Trim();
        }
        else
        {
            var parts = patient.User.FullName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            displayName = parts.Length > 1 ? $"{parts[0]} {parts[^1][0]}." : parts[0];
        }

        var review = new DoctorReview
        {
            DoctorId = dto.DoctorId,
            PatientId = patient.Id,
            AppointmentId = dto.AppointmentId,
            Rating = dto.Rating,
            Title = string.IsNullOrWhiteSpace(dto.Title) ? null : dto.Title.Trim(),
            Comment = dto.Comment.Trim(),
            PatientDisplayName = displayName,
            IsVerifiedAppointment = true,
            IsDemo = false,
            ModerationStatus = ReviewModerationStatus.Approved,
            HelpfulCount = 0,
            CreatedAt = DateTime.UtcNow
        };

        _context.DoctorReviews.Add(review);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Patient {PatientId} submitted verified review {ReviewId} for Doctor {DoctorId}.", patient.Id, review.Id, dto.DoctorId);

        return Result<DoctorReviewDto>.Success(MapToDto(review, false));
    }

    public async Task<Result<ReviewHelpfulVoteResultDto>> ToggleHelpfulVoteAsync(int reviewId, string currentUserId, string? ipAddress = null)
    {
        var review = await _context.DoctorReviews.FirstOrDefaultAsync(r => r.Id == reviewId);
        if (review == null)
        {
            return Result<ReviewHelpfulVoteResultDto>.Failure("Review not found.");
        }

        var existingVote = await _context.DoctorReviewHelpfulVotes
            .FirstOrDefaultAsync(v => v.ReviewId == reviewId && v.UserId == currentUserId);

        bool votedNow;
        if (existingVote != null)
        {
            _context.DoctorReviewHelpfulVotes.Remove(existingVote);
            votedNow = false;
        }
        else
        {
            string? ipHash = null;
            if (!string.IsNullOrEmpty(ipAddress))
            {
                using var sha = SHA256.Create();
                var hashBytes = sha.ComputeHash(Encoding.UTF8.GetBytes(ipAddress));
                ipHash = Convert.ToHexString(hashBytes);
            }

            var vote = new DoctorReviewHelpfulVote
            {
                ReviewId = reviewId,
                UserId = currentUserId,
                UserIpHash = ipHash,
                CreatedAtUtc = DateTime.UtcNow
            };
            _context.DoctorReviewHelpfulVotes.Add(vote);
            votedNow = true;
        }

        await _context.SaveChangesAsync();

        // Atomically recompute helpful count from database votes table
        review.HelpfulCount = await _context.DoctorReviewHelpfulVotes.CountAsync(v => v.ReviewId == reviewId);
        await _context.SaveChangesAsync();

        return Result<ReviewHelpfulVoteResultDto>.Success(new ReviewHelpfulVoteResultDto
        {
            Success = true,
            ReviewId = reviewId,
            HelpfulCount = review.HelpfulCount,
            HasVoted = votedNow,
            Message = votedNow ? "Marked review as helpful." : "Removed helpful vote."
        });
    }

    public async Task<Result<DoctorReviewDto>> DoctorReplyAsync(DoctorReplyReviewDto dto, string doctorUserId)
    {
        var doctor = await _context.Doctors
            .Include(d => d.User)
            .FirstOrDefaultAsync(d => d.UserId == doctorUserId);

        if (doctor == null)
        {
            return Result<DoctorReviewDto>.Failure("Doctor profile not found.");
        }

        var review = await _context.DoctorReviews
            .Include(r => r.Doctor).ThenInclude(d => d.User)
            .FirstOrDefaultAsync(r => r.Id == dto.ReviewId);

        if (review == null)
        {
            return Result<DoctorReviewDto>.Failure("Review not found.");
        }

        if (review.DoctorId != doctor.Id)
        {
            return Result<DoctorReviewDto>.Failure("You can only respond to reviews submitted for your own profile.");
        }

        if (string.IsNullOrWhiteSpace(dto.Response) || dto.Response.Trim().Length < 5)
        {
            return Result<DoctorReviewDto>.Failure("Response must be at least 5 characters long.");
        }

        if (dto.Response.Trim().Length > 2000)
        {
            return Result<DoctorReviewDto>.Failure("Response cannot exceed 2000 characters.");
        }

        review.DoctorResponse = dto.Response.Trim();
        review.DoctorResponseDateUtc = DateTime.UtcNow;
        review.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        _logger.LogInformation("Doctor {DoctorId} replied to review {ReviewId}.", doctor.Id, review.Id);

        return Result<DoctorReviewDto>.Success(MapToDto(review, false));
    }

    public async Task<Result<DoctorReviewDto>> AdminModerateReviewAsync(AdminModerateReviewDto dto, string adminUserId)
    {
        var review = await _context.DoctorReviews
            .Include(r => r.Doctor).ThenInclude(d => d.User)
            .FirstOrDefaultAsync(r => r.Id == dto.ReviewId);

        if (review == null)
        {
            return Result<DoctorReviewDto>.Failure("Review not found.");
        }

        if (dto.NewStatus == ReviewModerationStatus.Hidden)
        {
            if (string.IsNullOrWhiteSpace(dto.Reason) || dto.Reason.Trim().Length < 5)
            {
                return Result<DoctorReviewDto>.Failure("A valid documented moderation reason (at least 5 characters) is required to hide a review.");
            }
        }

        review.ModerationStatus = dto.NewStatus;
        review.ModeratedByAdminId = adminUserId;
        review.ModeratedAtUtc = DateTime.UtcNow;
        review.ModerationReason = dto.Reason?.Trim();
        review.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        _logger.LogInformation("Admin {AdminId} updated moderation status of review {ReviewId} to {Status}.", adminUserId, review.Id, dto.NewStatus);

        return Result<DoctorReviewDto>.Success(MapToDto(review, false));
    }

    public async Task<PagedResult<DoctorReviewDto>> GetAdminReviewQueueAsync(
        ReviewModerationStatus? status = null,
        int? doctorId = null,
        int pageNumber = 1,
        int pageSize = 20)
    {
        var query = _context.DoctorReviews
            .AsNoTracking()
            .Include(r => r.Doctor).ThenInclude(d => d.User)
            .Include(r => r.Doctor).ThenInclude(d => d.Specialization)
            .AsQueryable();

        if (status.HasValue)
        {
            query = query.Where(r => r.ModerationStatus == status.Value);
        }

        if (doctorId.HasValue)
        {
            query = query.Where(r => r.DoctorId == doctorId.Value);
        }

        query = query.OrderByDescending(r => r.CreatedAt);

        var totalCount = await query.CountAsync();
        var p = pageNumber < 1 ? 1 : pageNumber;
        var s = pageSize < 1 ? 20 : pageSize;

        var items = await query.Skip((p - 1) * s).Take(s).ToListAsync();
        var dtos = items.Select(r => MapToDto(r, false)).ToList();

        return new PagedResult<DoctorReviewDto>(dtos, totalCount, p, s);
    }

    public async Task<int> PurgeDemoReviewsAsync()
    {
        var demoReviews = await _context.DoctorReviews.Where(r => r.IsDemo).ToListAsync();
        if (demoReviews.Count == 0) return 0;

        _context.DoctorReviews.RemoveRange(demoReviews);
        await _context.SaveChangesAsync();
        _logger.LogInformation("Purged {Count} demo reviews from database.", demoReviews.Count);
        return demoReviews.Count;
    }

    public async Task<int> SeedDemoReviewsAsync(bool force = false)
    {
        if (!IsDemoModeAllowed())
        {
            _logger.LogWarning("Demo reviews seeding rejected: Demo mode is only permitted in Development or an explicit Demo environment.");
            return 0;
        }

        var doctors = await _context.Doctors
            .Include(d => d.User)
            .Include(d => d.Specialization)
            .Where(d => d.IsApproved)
            .ToListAsync();

        if (doctors.Count == 0) return 0;

        int totalSeeded = 0;
        var now = DateTime.UtcNow;

        var demoReviewTemplates = GetDemoReviewTemplates();

        foreach (var doctor in doctors)
        {
            if (!force)
            {
                var existingDemoCount = await _context.DoctorReviews
                    .CountAsync(r => r.DoctorId == doctor.Id && r.IsDemo);

                if (existingDemoCount >= 8) continue;
            }

            int index = 0;
            foreach (var template in demoReviewTemplates)
            {
                var reviewDate = now.AddDays(-((index * 9) + 3));
                var review = new DoctorReview
                {
                    DoctorId = doctor.Id,
                    PatientId = null,
                    AppointmentId = null,
                    Rating = template.Rating,
                    Title = template.Title,
                    Comment = template.Comment,
                    PatientDisplayName = template.PatientName,
                    IsVerifiedAppointment = false,
                    IsDemo = true,
                    ModerationStatus = ReviewModerationStatus.Approved,
                    HelpfulCount = template.HelpfulCount,
                    DoctorResponse = template.DoctorReply,
                    DoctorResponseDateUtc = template.DoctorReply != null ? reviewDate.AddDays(1) : null,
                    CreatedAt = reviewDate
                };

                _context.DoctorReviews.Add(review);
                totalSeeded++;
                index++;
            }
        }

        if (totalSeeded > 0)
        {
            await _context.SaveChangesAsync();
            _logger.LogInformation("Successfully seeded {Count} demo reviews across approved doctors.", totalSeeded);
        }

        return totalSeeded;
    }

    private static DoctorReviewDto MapToDto(DoctorReview r, bool hasUserVoted)
    {
        return new DoctorReviewDto
        {
            Id = r.Id,
            DoctorId = r.DoctorId,
            DoctorName = r.Doctor?.User?.FullName ?? "Doctor",
            DoctorSpecialization = r.Doctor?.Specialization?.Name ?? string.Empty,
            PatientId = r.PatientId,
            AppointmentId = r.AppointmentId,
            Rating = r.Rating,
            Title = r.Title,
            Comment = r.Comment,
            PatientDisplayName = r.PatientDisplayName,
            IsVerifiedAppointment = r.IsVerifiedAppointment,
            IsDemo = r.IsDemo,
            ModerationStatus = r.ModerationStatus,
            HelpfulCount = r.HelpfulCount,
            HasUserVotedHelpful = hasUserVoted,
            DoctorResponse = r.DoctorResponse,
            DoctorResponseDateUtc = r.DoctorResponseDateUtc,
            ModeratedByAdminId = r.ModeratedByAdminId,
            ModeratedAtUtc = r.ModeratedAtUtc,
            ModerationReason = r.ModerationReason,
            CreatedAt = r.CreatedAt,
            UpdatedAt = r.UpdatedAt
        };
    }

    private record DemoTemplate(
        string PatientName,
        int Rating,
        string Title,
        string Comment,
        int HelpfulCount,
        string? DoctorReply
    );

    private static List<DemoTemplate> GetDemoReviewTemplates() => new()
    {
        new(
            "Tarek Mansour",
            5,
            "استماع دقيق وتشخيص ممتاز",
            "طبيب متميز جداً واستمع لجميع الأعراض باهتمام وصبر شديد. شرح لي تفاصيل الفحوصات بأسلوب مبسط وواضح والحمد لله بدأت أشعر بتحسن كبير مع العلاج المقترح.",
            12,
            "شكراً جزيلاً أستاذ طارق، ونسأل الله لكم دوام الصحة والعافية."
        ),
        new(
            "Nourhan Khalil",
            5,
            "Very Professional & Thorough",
            "Dr. provided a very comprehensive examination and took the time to answer all my questions regarding medication interactions. Clinic was spotless and staff was welcoming.",
            9,
            null
        ),
        new(
            "Hany Samir",
            4,
            "طبيب ماهر ولكن انتظار بسيط",
            "الدكتور ممتاز وأسلوبه في الكشف راقي جداً ولديه ضمير مهني عالي. الملاحظة الوحيدة هي الانتظار لمدة 20 دقيقة عن موعد الحجز بسبب ازدحام العيادة، لكن الكشف يستحق.",
            7,
            "نشكركم على ثقتكم الغالية أستاذ هاني، ونعمل باستمرار على تقليل أوقات الانتظار لتوفير أفضل تجربة."
        ),
        new(
            "Sarah Jenkins",
            5,
            "Excellent Bedside Manner",
            "Wonderful experience from start to finish. The doctor was punctual, extremely knowledgeable, and provided clear follow-up guidelines. Highly recommend!",
            14,
            null
        ),
        new(
            "Mahmoud El-Sayed",
            3,
            "كشف جيد لكن العيادة مزدحمة",
            "الدكتور شاطر وأعطاني العلاج المناسب تماماً، ولكن وقت الكشف كان سريعاً بعض الشيء والعيادة كانت شديدة الازدحام. نأمل تنظيم المواعيد بشكل أفضل مستقبلاً.",
            4,
            null
        ),
        new(
            "Dina Fahmy",
            5,
            "رعاية فائقة ومتابعة ممتازة",
            "من أفضل الأطباء الذين تعاملت معهم. اهتمام بأدق التفاصيل الطبية وشرح كامل لخطوات الوقاية وليس العلاج الدوائي فقط. جزاكم الله كل خير.",
            11,
            "سعدنا بخدمتك أستاذة دينا ونتمنى لكِ دوام العافية."
        ),
        new(
            "Amira Gamil",
            4,
            "تشخيص دقيق ومعاملة محترمة",
            "تعامل راقي جداً من الطبيب وطاقم التمريض. الروشتة كانت واضحة بدون أدوية إضافية غير ضرورية. تجربة إيجابية ومطمئنة.",
            6,
            null
        ),
        new(
            "Karim Nabil",
            2,
            "تأخر في موعد الدخول",
            "الدكتور محترم ومهني عند الدخول، لكن الانتظار تجاوز 40 دقيقة بعد الموعد المحدد للحجز بالرغم من الحضور قبل الموعد. يرجى احترام مواعيد الحجز المسبق.",
            5,
            "نعتذر بشدة عن التأخير غير المقصود أستاذ كريم، وكان ذلك بسبب حالة طارئة استدعت تدخلاً عاجلاً. نعدكم بتقديم تجربة أفضل في زيارتكم القادمة."
        ),
        new(
            "Yasmine Radwan",
            5,
            "Great explanation and reassurance",
            "The doctor thoroughly explained my test results and calmed all my anxieties. Prescribed exactly what was needed without ordering redundant tests. Very grateful!",
            8,
            null
        ),
        new(
            "Omar Hassan",
            1,
            "وقت الكشف كان قصيراً جداً",
            "مع كامل الاحترام للدكتور، شعرت أن الزيارة كانت متسرعة جداً ولم يتوفر وقت كافٍ لمناقشة كافة التساؤلات، وتكرر الرد على الهاتف أثناء الكشف.",
            3,
            "نأسف بشدة لسماع ذلك أستاذ عمر، ونأخذ ملاحظاتكم بعين الاعتبار لتحسين جودة واستمرارية وقت الاستشارة الطبية."
        ),
        new(
            "Farida Zaher",
            5,
            "عيادة متميزة وطبيب مخلص",
            "تجربة نموذجية ومستوى نظافة وتعقيم عالي. الدكتور شرح كل خطوة في خطة العلاج بوضوح تام، وفريق الاستقبال متعاون جداً.",
            10,
            null
        )
    };
}
