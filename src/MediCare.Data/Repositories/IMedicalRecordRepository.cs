using MediCare.Data.Entities;

namespace MediCare.Data.Repositories;

public interface IMedicalRecordRepository : IRepository<MedicalRecord>
{
    Task<MedicalRecord?> GetByIdWithDetailsAsync(int id);
    Task<MedicalRecord?> GetByAppointmentIdWithDetailsAsync(int appointmentId);
    Task<List<MedicalRecord>> GetPatientHistoryAsync(int patientId);
}
