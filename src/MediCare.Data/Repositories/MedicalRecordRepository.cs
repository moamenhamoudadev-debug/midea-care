using MediCare.Data.Context;
using MediCare.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace MediCare.Data.Repositories;

public class MedicalRecordRepository : Repository<MedicalRecord>, IMedicalRecordRepository
{
    public MedicalRecordRepository(ApplicationDbContext context) : base(context)
    {
    }

    public async Task<MedicalRecord?> GetByIdWithDetailsAsync(int id)
    {
        return await _context.MedicalRecords
            .Include(m => m.Appointment)
            .Include(m => m.Doctor).ThenInclude(d => d.User)
            .Include(m => m.Doctor).ThenInclude(d => d.Specialization)
            .Include(m => m.Patient).ThenInclude(p => p.User)
            .Include(m => m.Prescription).ThenInclude(p => p!.Items)
            .FirstOrDefaultAsync(m => m.Id == id);
    }

    public async Task<MedicalRecord?> GetByAppointmentIdWithDetailsAsync(int appointmentId)
    {
        return await _context.MedicalRecords
            .Include(m => m.Appointment)
            .Include(m => m.Doctor).ThenInclude(d => d.User)
            .Include(m => m.Doctor).ThenInclude(d => d.Specialization)
            .Include(m => m.Patient).ThenInclude(p => p.User)
            .Include(m => m.Prescription).ThenInclude(p => p!.Items)
            .FirstOrDefaultAsync(m => m.AppointmentId == appointmentId);
    }

    public async Task<List<MedicalRecord>> GetPatientHistoryAsync(int patientId)
    {
        return await _context.MedicalRecords
            .AsNoTracking()
            .Include(m => m.Appointment)
            .Include(m => m.Doctor).ThenInclude(d => d.User)
            .Include(m => m.Doctor).ThenInclude(d => d.Specialization)
            .Include(m => m.Prescription)
            .Where(m => m.PatientId == patientId)
            .OrderByDescending(m => m.Appointment.AppointmentDate)
            .ThenByDescending(m => m.Appointment.StartTime)
            .ToListAsync();
    }
}
