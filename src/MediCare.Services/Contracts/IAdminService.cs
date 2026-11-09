using MediCare.Services.Common;
using MediCare.Services.DTOs;

namespace MediCare.Services.Contracts;

public interface IAdminService
{
    Task<Result<List<DoctorApprovalSummaryDto>>> GetPendingDoctorsAsync();
    Task<Result> ApproveDoctorAsync(int doctorId);
    Task<Result> RejectDoctorAsync(int doctorId, string? reason);
    Task<Result<AdminDashboardMetricsDto>> GetDashboardMetricsAsync();
    Task<Result<byte[]>> ExportAppointmentsCsvAsync();
    Task<Result<byte[]>> ExportAppointmentsExcelAsync();
    Task<Result<byte[]>> ExportAppointmentsPdfAsync();

    Task<Result<List<AdminPatientSummaryDto>>> GetPatientsAsync(string? searchTerm = null);
    Task<Result<PagedResult<AdminPatientSummaryDto>>> GetPatientsPagedAsync(string? searchTerm = null, int page = 1, int pageSize = 20);
    Task<Result> TogglePatientLockoutAsync(int patientId, bool lockout);

    Task<Result> CreatePharmacistAsync(CreatePharmacistDto dto, string createdByAdminId);
    Task<Result<List<PharmacistSummaryDto>>> GetPharmacistsAsync();

    Task<Result<List<SpecializationDto>>> GetAllSpecializationsAsync();
    Task<Result<SpecializationDto>> GetSpecializationByIdAsync(int id);
    Task<Result<int>> CreateSpecializationAsync(CreateSpecializationDto dto);
    Task<Result> UpdateSpecializationAsync(int id, UpdateSpecializationDto dto);
    Task<Result> DeleteSpecializationAsync(int id);
}
