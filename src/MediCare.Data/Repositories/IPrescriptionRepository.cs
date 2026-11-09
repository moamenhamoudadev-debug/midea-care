using MediCare.Data.Entities;

namespace MediCare.Data.Repositories;

public interface IPrescriptionRepository : IRepository<Prescription>
{
    Task<Prescription?> GetByIdWithDetailsAsync(int id);
    Task<Prescription?> GetByAppointmentIdWithDetailsAsync(int appointmentId);
    Task<Prescription?> GetByTokenWithDetailsAsync(string token);
}
