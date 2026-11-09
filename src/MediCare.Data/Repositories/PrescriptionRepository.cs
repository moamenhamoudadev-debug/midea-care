using MediCare.Data.Context;
using MediCare.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace MediCare.Data.Repositories;

public class PrescriptionRepository : Repository<Prescription>, IPrescriptionRepository
{
    public PrescriptionRepository(ApplicationDbContext context) : base(context)
    {
    }

    public async Task<Prescription?> GetByIdWithDetailsAsync(int id)
    {
        return await _context.Prescriptions
            .Include(p => p.Doctor).ThenInclude(d => d.User)
            .Include(p => p.Doctor).ThenInclude(d => d.Specialization)
            .Include(p => p.Patient).ThenInclude(pt => pt.User)
            .Include(p => p.Items)
            .Include(p => p.MedicalRecord).ThenInclude(m => m.Appointment)
            .FirstOrDefaultAsync(p => p.Id == id);
    }

    public async Task<Prescription?> GetByAppointmentIdWithDetailsAsync(int appointmentId)
    {
        return await _context.Prescriptions
            .Include(p => p.Doctor).ThenInclude(d => d.User)
            .Include(p => p.Doctor).ThenInclude(d => d.Specialization)
            .Include(p => p.Patient).ThenInclude(pt => pt.User)
            .Include(p => p.Items)
            .Include(p => p.MedicalRecord)
            .FirstOrDefaultAsync(p => p.MedicalRecord.AppointmentId == appointmentId);
    }

    public async Task<Prescription?> GetByTokenWithDetailsAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;

        return await _context.Prescriptions
            .Include(p => p.Doctor).ThenInclude(d => d.User)
            .Include(p => p.Doctor).ThenInclude(d => d.Specialization)
            .Include(p => p.Patient).ThenInclude(pt => pt.User)
            .Include(p => p.Items)
            .Include(p => p.MedicalRecord).ThenInclude(m => m.Appointment)
            .Include(p => p.DispensedByUser)
            .FirstOrDefaultAsync(p => p.VerificationToken == token);
    }
}
