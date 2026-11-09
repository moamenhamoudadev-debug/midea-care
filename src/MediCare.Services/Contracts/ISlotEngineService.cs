using MediCare.Data.Entities;
using MediCare.Services.DTOs;

namespace MediCare.Services.Contracts;

/// <summary>
/// Slot calculation engine responsible for generating available appointment intervals.
/// Operates in Local Clinic Time (Egypt Standard Time).
/// </summary>
public interface ISlotEngineService
{
    Task<List<SlotDto>> GetAvailableSlotsAsync(int doctorId, DateTime date);
    Task<List<SlotDto>> GetAvailableSlotsRangeAsync(int doctorId, DateTime startDate, DateTime endDate);

    /// <summary>
    /// Pure computation method calculating slots in memory from pre-loaded data.
    /// </summary>
    List<SlotDto> ComputeSlots(
        DateTime date,
        int slotDurationMinutes,
        IEnumerable<WorkingHours> workingHours,
        IEnumerable<DoctorLeave> leaves,
        IEnumerable<Appointment> existingAppointments,
        DateTime currentClinicTime);
}
