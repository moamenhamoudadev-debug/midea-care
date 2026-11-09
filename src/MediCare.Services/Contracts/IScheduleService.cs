using MediCare.Services.Common;
using MediCare.Services.DTOs;

namespace MediCare.Services.Contracts;

public interface IScheduleService
{
    Task<Result<List<WorkingHoursDto>>> GetWorkingHoursAsync(int doctorId);
    Task<Result<WorkingHoursDto>> GetWorkingHoursByIdAsync(int id, int doctorId);
    Task<Result<int>> AddWorkingHoursAsync(WorkingHoursDto dto, int doctorId);
    Task<Result> UpdateWorkingHoursAsync(WorkingHoursDto dto, int doctorId);
    Task<Result> DeleteWorkingHoursAsync(int id, int doctorId);

    Task<Result<List<DoctorLeaveDto>>> GetDoctorLeavesAsync(int doctorId);
    Task<Result<DoctorLeaveCreateResultDto>> AddDoctorLeaveAsync(DoctorLeaveDto dto, int doctorId);
    Task<Result> DeleteDoctorLeaveAsync(int id, int doctorId);
    Task<Result<List<LeaveConflictWarningDto>>> CheckLeaveConflictsAsync(int doctorId, DateTime startDate, DateTime endDate);
}
