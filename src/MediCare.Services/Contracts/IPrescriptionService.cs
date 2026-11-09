using MediCare.Services.Common;
using MediCare.Services.DTOs;

namespace MediCare.Services.Contracts;

public interface IPrescriptionService
{
    Task<Result<PrescriptionDetailsDto>> GetPrescriptionForPrintAsync(int prescriptionId, string userId, bool isDoctor, bool isPatient, bool isAdmin);
    Task<Result<PrescriptionDetailsDto>> GetPrescriptionByAppointmentIdAsync(int appointmentId, string userId, bool isDoctor, bool isPatient, bool isAdmin);
    Task<Result<PrescriptionVerificationDto>> VerifyPrescriptionByTokenAsync(string token);
    Task<Result<PharmacistPrescriptionReviewDto>> GetPrescriptionForPharmacistAsync(string token);
    Task<Result> DispensePrescriptionAsync(string token, string pharmacistUserId, string? pharmacyNotes);
}
