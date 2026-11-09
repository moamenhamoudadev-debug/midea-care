using MediCare.Services.Common;
using MediCare.Services.DTOs;

namespace MediCare.Services.Contracts;

public interface IDoctorService
{
    Task<PagedResult<DoctorSummaryDto>> SearchDoctorsAsync(DoctorFilterDto filter);
    Task<Result<DoctorDetailDto>> GetDoctorDetailsAsync(int id);
    Task<List<SpecializationDto>> GetSpecializationsAsync();
    Task<Result<DoctorDetailDto>> GetDoctorByUserIdAsync(string userId);
    Task<Result<int>> GetDoctorIdByUserIdAsync(string userId);
}
