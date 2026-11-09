using FluentValidation;
using MediCare.Data.UnitOfWork;
using MediCare.Services.Common;
using MediCare.Services.Contracts;
using MediCare.Services.DTOs;

namespace MediCare.Services.Implementations;

public class DoctorService : IDoctorService
{
    private readonly IUnitOfWork _uow;
    private readonly IValidator<DoctorFilterDto> _filterValidator;

    public DoctorService(IUnitOfWork uow, IValidator<DoctorFilterDto> filterValidator)
    {
        _uow = uow;
        _filterValidator = filterValidator;
    }

    public async Task<PagedResult<DoctorSummaryDto>> SearchDoctorsAsync(DoctorFilterDto filter)
    {
        await _filterValidator.ValidateAsync(filter);

        var page = filter.Page < 1 ? 1 : filter.Page;
        var pageSize = filter.PageSize < 1 ? 6 : filter.PageSize;

        var (doctors, totalCount) = await _uow.Doctors.SearchApprovedDoctorsAsync(
            filter.SpecializationId,
            filter.MaxFee,
            filter.AvailableDay,
            filter.SearchTerm,
            filter.Governorate,
            page,
            pageSize);

        var dtos = doctors.Select(d =>
        {
            var dto = new DoctorSummaryDto
            {
                Id = d.Id,
                FullName = d.User.FullName,
                SpecializationId = d.SpecializationId,
                SpecializationName = d.Specialization.Name,
                ConsultationFee = d.ConsultationFee,
                Governorate = d.Governorate,
                ProfileImageUrl = "/images/doctors/doctor-default.svg",
                Bio = d.Bio,
                WorkingDays = d.WorkingHours.Select(w => w.DayOfWeek).Distinct().OrderBy(day => day).ToList()
            };
            EnrichDoctorSummary(d, dto);
            return dto;
        }).ToList();

        return new PagedResult<DoctorSummaryDto>(dtos, totalCount, page, pageSize);
    }

    public async Task<Result<DoctorDetailDto>> GetDoctorDetailsAsync(int id)
    {
        var doctor = await _uow.Doctors.GetDoctorWithScheduleAsync(id);
        if (doctor == null)
        {
            return Result<DoctorDetailDto>.Failure("Doctor not found or account is not approved.");
        }

        var dto = new DoctorDetailDto
        {
            Id = doctor.Id,
            FullName = doctor.User.FullName,
            Email = doctor.User.Email ?? string.Empty,
            PhoneNumber = doctor.User.PhoneNumber,
            SpecializationId = doctor.SpecializationId,
            SpecializationName = doctor.Specialization.Name,
            LicenseNumber = doctor.LicenseNumber,
            ConsultationFee = doctor.ConsultationFee,
            SlotDurationMinutes = doctor.SlotDurationMinutes,
            ProfileImageUrl = "/images/doctors/doctor-default.svg",
            Bio = doctor.Bio,
            WorkingHours = doctor.WorkingHours
                .OrderBy(w => w.DayOfWeek)
                .ThenBy(w => w.StartTime)
                .Select(w => new WorkingHourDto
                {
                    DayOfWeek = w.DayOfWeek,
                    StartTime = w.StartTime,
                    EndTime = w.EndTime
                }).ToList()
        };

        EnrichDoctorDetail(doctor, dto);
        return Result<DoctorDetailDto>.Success(dto);
    }

    public async Task<List<SpecializationDto>> GetSpecializationsAsync()
    {
        var specs = await _uow.Specializations.GetAllAsync();
        return specs
            .OrderBy(s => s.Name)
            .Select(s => new SpecializationDto
            {
                Id = s.Id,
                Name = s.Name,
                Description = s.Description
            }).ToList();
    }

    public async Task<Result<DoctorDetailDto>> GetDoctorByUserIdAsync(string userId)
    {
        var doctor = await _uow.Doctors.GetByUserIdAsync(userId);
        if (doctor == null)
        {
            return Result<DoctorDetailDto>.Failure("Doctor profile not found.");
        }

        var dto = new DoctorDetailDto
        {
            Id = doctor.Id,
            FullName = doctor.User.FullName,
            Email = doctor.User.Email ?? string.Empty,
            PhoneNumber = doctor.User.PhoneNumber,
            SpecializationId = doctor.SpecializationId,
            SpecializationName = doctor.Specialization.Name,
            LicenseNumber = doctor.LicenseNumber,
            ConsultationFee = doctor.ConsultationFee,
            SlotDurationMinutes = doctor.SlotDurationMinutes,
            ProfileImageUrl = doctor.ProfileImageUrl,
            Bio = doctor.Bio,
            WorkingHours = doctor.WorkingHours
                .OrderBy(w => w.DayOfWeek)
                .ThenBy(w => w.StartTime)
                .Select(w => new WorkingHourDto
                {
                    DayOfWeek = w.DayOfWeek,
                    StartTime = w.StartTime,
                    EndTime = w.EndTime
                }).ToList()
        };

        EnrichDoctorDetail(doctor, dto);
        return Result<DoctorDetailDto>.Success(dto);
    }

    public async Task<Result<int>> GetDoctorIdByUserIdAsync(string userId)
    {
        var doctor = await _uow.Doctors.GetByUserIdAsync(userId);
        if (doctor == null)
        {
            return Result<int>.Failure("Doctor profile not found.");
        }

        return Result<int>.Success(doctor.Id);
    }

    private static void EnrichDoctorSummary(MediCare.Data.Entities.Doctor d, DoctorSummaryDto dto)
    {
        var meta = GetDoctorProfileMetadata(d);
        dto.Rating = meta.Rating;
        dto.ReviewCount = meta.ReviewCount;
        dto.Governorate = meta.Governorate;
        dto.ClinicAddress = meta.ClinicAddress;
        dto.Title = meta.Title;
        dto.ExperienceYears = meta.ExperienceYears;

        dto.AcceptsInsurance = false;
        dto.InsuranceDiscountPercentage = 0;
        dto.DiscountedFee = dto.ConsultationFee;
        dto.InsuranceProviders = new List<string>();
        dto.InsuranceBadge = string.Empty;
        dto.TopReviewComment = null;
        dto.TopReviewPatient = null;
    }

    private static void EnrichDoctorDetail(MediCare.Data.Entities.Doctor d, DoctorDetailDto dto)
    {
        var meta = GetDoctorProfileMetadata(d);
        dto.Rating = meta.Rating;
        dto.ReviewCount = meta.ReviewCount;
        dto.Governorate = meta.Governorate;
        dto.ClinicAddress = meta.ClinicAddress;
        dto.Title = meta.Title;
        dto.ExperienceYears = meta.ExperienceYears;
        dto.AcademicDegree = meta.AcademicDegree;
        dto.SubSpecialties = meta.SubSpecialties;
        dto.Reviews = meta.Reviews;

        dto.AcceptsInsurance = false;
        dto.InsuranceDiscountPercentage = 0;
        dto.DiscountedFee = dto.ConsultationFee;
        dto.InsuranceProviders = new List<string>();
        dto.InsuranceBadge = string.Empty;
    }

    private sealed class ProfileMetadata
    {
        public double Rating { get; init; }
        public int ReviewCount { get; init; }
        public string Governorate { get; init; } = string.Empty;
        public string ClinicAddress { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;
        public int ExperienceYears { get; init; }
        public string AcademicDegree { get; init; } = string.Empty;
        public List<string> SubSpecialties { get; init; } = new();
        public List<DoctorReviewDto> Reviews { get; init; } = new();
    }

    // Directory claims must come from stored, reviewable evidence. The current schema
    // has no provenance or authentic review records, so this service exposes none.
    private static ProfileMetadata GetDoctorProfileMetadata(MediCare.Data.Entities.Doctor d) => new()
    {
        Governorate = d.Governorate
    };
}
