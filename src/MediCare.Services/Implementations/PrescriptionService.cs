using MediCare.Data.UnitOfWork;
using MediCare.Services.Common;
using MediCare.Services.Contracts;
using MediCare.Services.DTOs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MediCare.Services.Implementations;

public class PrescriptionService : IPrescriptionService
{
    private readonly IUnitOfWork _uow;
    private readonly IClinicClock _clinicClock;
    private readonly ILogger<PrescriptionService> _logger;
    private readonly int _validityDays;

    public PrescriptionService(
        IUnitOfWork uow,
        IClinicClock clinicClock,
        ILogger<PrescriptionService> logger,
        IOptions<PrescriptionSettings>? options = null)
    {
        _uow = uow;
        _clinicClock = clinicClock;
        _logger = logger;
        _validityDays = options?.Value?.PrescriptionValidityDays > 0 ? options.Value.PrescriptionValidityDays : 30;
    }

    public async Task<Result<PrescriptionDetailsDto>> GetPrescriptionForPrintAsync(
        int prescriptionId, string userId, bool isDoctor, bool isPatient, bool isAdmin)
    {
        var prescription = await _uow.Prescriptions.GetByIdWithDetailsAsync(prescriptionId);
        if (prescription == null)
        {
            return Result<PrescriptionDetailsDto>.Failure("Prescription not found.");
        }

        // IDOR Authorization Check (BIZ-09: Admins must not have access to prescriptions)
        bool isAuthorized = false;
        if (isDoctor && prescription.Doctor?.UserId == userId)
        {
            isAuthorized = true;
        }
        else if (isPatient && prescription.Patient?.UserId == userId)
        {
            isAuthorized = true;
        }

        if (!isAuthorized)
        {
            _logger.LogWarning("Security IDOR: User {UserId} attempted unauthorized access to Prescription {PrescriptionId} (PatientOwner: {PatientUserId}, DoctorOwner: {DoctorUserId})",
                userId, prescriptionId, prescription.Patient?.UserId, prescription.Doctor?.UserId);
            return Result<PrescriptionDetailsDto>.Failure("Forbidden: You are not authorized to view this prescription.");
        }

        int? age = null;
        if (prescription.Patient != null && prescription.Patient.DateOfBirth != default)
        {
            var birth = prescription.Patient.DateOfBirth;
            var now = _clinicClock.Today;
            int calculatedAge = now.Year - birth.Year;
            if (birth.Date > now.AddYears(-calculatedAge)) calculatedAge--;
            age = calculatedAge;
        }

        var validUntil = prescription.PrescriptionDate.AddDays(_validityDays);
        bool isExpired = _clinicClock.Now > validUntil;

        var dto = new PrescriptionDetailsDto
        {
            Id = prescription.Id,
            MedicalRecordId = prescription.MedicalRecordId,
            AppointmentId = prescription.MedicalRecord?.AppointmentId ?? 0,
            PrescriptionDate = prescription.PrescriptionDate,
            PrescriptionValidityDays = _validityDays,
            IsExpired = isExpired,
            DoctorName = prescription.Doctor?.User?.FullName ?? "Unknown Doctor",
            DoctorLicense = prescription.Doctor?.LicenseNumber ?? string.Empty,
            Specialization = prescription.Doctor?.Specialization?.Name ?? string.Empty,
            PatientName = prescription.Patient?.User?.FullName ?? "Unknown Patient",
            PatientAge = age,
            PatientGender = prescription.Patient?.Gender,
            Notes = prescription.Notes,
            VerificationToken = prescription.VerificationToken,
            IsDispensed = prescription.IsDispensed,
            DispensedAt = prescription.DispensedAt,
            DispensedNotes = prescription.PharmacyNotes,
            Items = prescription.Items.Select(i => new PrescriptionItemDto
            {
                Id = i.Id,
                MedicationName = i.MedicationName,
                Dosage = i.Dosage,
                Frequency = i.Frequency,
                DurationDays = i.DurationDays,
                Instructions = i.Instructions
            }).ToList()
        };

        return Result<PrescriptionDetailsDto>.Success(dto);
    }

    public async Task<Result<PrescriptionDetailsDto>> GetPrescriptionByAppointmentIdAsync(
        int appointmentId, string userId, bool isDoctor, bool isPatient, bool isAdmin)
    {
        var prescription = await _uow.Prescriptions.GetByAppointmentIdWithDetailsAsync(appointmentId);
        if (prescription == null)
        {
            return Result<PrescriptionDetailsDto>.Failure("Prescription not found for this appointment.");
        }

        return await GetPrescriptionForPrintAsync(prescription.Id, userId, isDoctor, isPatient, isAdmin);
    }

    public async Task<Result<PrescriptionVerificationDto>> VerifyPrescriptionByTokenAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length != 32)
        {
            return Result<PrescriptionVerificationDto>.Failure("Invalid or unrecognized prescription verification token.");
        }

        var prescription = await _uow.Prescriptions.GetByTokenWithDetailsAsync(token);
        if (prescription == null)
        {
            // Generic failure: no difference between not found and malformed to prevent enumeration
            return Result<PrescriptionVerificationDto>.Failure("Invalid or unrecognized prescription verification token.");
        }

        var validUntil = prescription.PrescriptionDate.AddDays(_validityDays);
        bool isExpired = _clinicClock.Now > validUntil;
        var maskedName = isExpired ? string.Empty : PatientNameMasker.Mask(prescription.Patient?.User?.FullName);

        var dto = new PrescriptionVerificationDto
        {
            IsValid = true,
            IsExpired = isExpired,
            ValidUntil = validUntil,
            DoctorName = prescription.Doctor?.User?.FullName ?? "Physician",
            Specialization = prescription.Doctor?.Specialization?.Name ?? string.Empty,
            PrescriptionDate = prescription.PrescriptionDate,
            MaskedPatientName = maskedName,
            IsDispensed = prescription.IsDispensed,
            DispensedAt = prescription.DispensedAt
        };

        return Result<PrescriptionVerificationDto>.Success(dto);
    }

    public async Task<Result<PharmacistPrescriptionReviewDto>> GetPrescriptionForPharmacistAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length != 32)
        {
            return Result<PharmacistPrescriptionReviewDto>.Failure("Invalid or unrecognized prescription verification token.");
        }

        var prescription = await _uow.Prescriptions.GetByTokenWithDetailsAsync(token);
        if (prescription == null)
        {
            return Result<PharmacistPrescriptionReviewDto>.Failure("Prescription not found or invalid token.");
        }

        var validUntil = prescription.PrescriptionDate.AddDays(_validityDays);
        bool isExpired = _clinicClock.Now > validUntil;

        var dto = new PharmacistPrescriptionReviewDto
        {
            PrescriptionId = prescription.Id,
            VerificationToken = prescription.VerificationToken,
            DoctorName = prescription.Doctor?.User?.FullName ?? "Physician",
            Specialization = prescription.Doctor?.Specialization?.Name ?? string.Empty,
            MaskedPatientName = PatientNameMasker.Mask(prescription.Patient?.User?.FullName),
            PrescriptionDate = prescription.PrescriptionDate,
            IsExpired = isExpired,
            ValidUntil = validUntil,
            IsDispensed = prescription.IsDispensed,
            DispensedAt = prescription.DispensedAt,
            DispensedByName = prescription.DispensedByUser?.FullName,
            PharmacyNotes = prescription.PharmacyNotes,
            Items = prescription.Items.Select(i => new PrescriptionItemDto
            {
                Id = i.Id,
                MedicationName = i.MedicationName,
                Dosage = i.Dosage,
                Frequency = i.Frequency,
                DurationDays = i.DurationDays,
                Instructions = i.Instructions
            }).ToList()
        };

        return Result<PharmacistPrescriptionReviewDto>.Success(dto);
    }

    public async Task<Result> DispensePrescriptionAsync(string token, string pharmacistUserId, string? pharmacyNotes)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length != 32)
        {
            return Result.Failure("Invalid prescription verification token.");
        }

        var prescription = await _uow.Prescriptions.GetByTokenWithDetailsAsync(token);
        if (prescription == null)
        {
            return Result.Failure("Prescription not found or invalid verification token.");
        }

        // Validity / Expiry guard
        var validUntil = prescription.PrescriptionDate.AddDays(_validityDays);
        if (_clinicClock.Now > validUntil)
        {
            return Result.Failure($"لا يمكن صرف هذه الروشتة لانتهاء فترة صلاحيتها ({_validityDays} يوماً من تاريخ التحرير). (Prescription has expired; validity period is {_validityDays} days.)");
        }

        // Concurrency & idempotency guard: check if already dispensed
        if (prescription.IsDispensed)
        {
            return Result.Failure("Prescription has already been marked as dispensed.");
        }

        var now = _clinicClock.Now;
        prescription.IsDispensed = true;
        prescription.DispensedAt = now;
        prescription.DispensedByUserId = pharmacistUserId;
        prescription.PharmacyNotes = pharmacyNotes?.Trim();
        prescription.UpdatedAt = now;

        _uow.Prescriptions.Update(prescription);

        try
        {
            await _uow.CommitAsync();
            _logger.LogInformation("Prescription {PrescriptionId} dispensed by Pharmacist {UserId} at {DispensedAt}",
                prescription.Id, pharmacistUserId, now);
            return Result.Success();
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException ex)
        {
            _logger.LogWarning(ex, "Concurrency conflict: Prescription {PrescriptionId} was already updated concurrently.", prescription.Id);
            return Result.Failure("Prescription was already dispensed by another concurrent request.");
        }
    }
}
