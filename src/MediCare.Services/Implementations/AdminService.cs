using System.Globalization;
using System.Text;
using MediCare.Data.Entities;
using MediCare.Data.Enums;
using MediCare.Data.UnitOfWork;
using MediCare.Services.Common;
using MediCare.Services.Contracts;
using MediCare.Services.DTOs;
using Microsoft.Extensions.Logging;

using Microsoft.AspNetCore.Identity;

namespace MediCare.Services.Implementations;

public class AdminService : IAdminService
{
    private readonly IUnitOfWork _uow;
    private readonly IEmailService _emailService;
    private readonly IClinicClock _clinicClock;
    private readonly ILogger<AdminService> _logger;
    private readonly UserManager<ApplicationUser>? _userManager;

    public AdminService(
        IUnitOfWork uow,
        IEmailService emailService,
        IClinicClock clinicClock,
        ILogger<AdminService> logger,
        UserManager<ApplicationUser>? userManager = null)
    {
        _uow = uow;
        _emailService = emailService;
        _clinicClock = clinicClock;
        _logger = logger;
        _userManager = userManager;
    }

    public async Task<Result<List<DoctorApprovalSummaryDto>>> GetPendingDoctorsAsync()
    {
        var pendingWithDetails = await _uow.Doctors.GetPendingDoctorsWithDetailsAsync();
        if (pendingWithDetails == null || pendingWithDetails.Count == 0)
        {
            var pending = (await _uow.Doctors.FindAsync(d => !d.IsApproved))?.ToList();
            if (pending != null && pending.Count > 0)
            {
                pendingWithDetails = new List<Doctor>();
                foreach (var doc in pending)
                {
                    var full = await _uow.Doctors.GetDoctorWithDetailsAsync(doc.Id) ?? doc;
                    pendingWithDetails.Add(full);
                }
            }
            else
            {
                pendingWithDetails = new List<Doctor>();
            }
        }

        var resultList = pendingWithDetails.Select(fullDoc => new DoctorApprovalSummaryDto
        {
            Id = fullDoc.Id,
            UserId = fullDoc.UserId,
            FullName = fullDoc.User?.FullName ?? "Unknown Doctor",
            Email = fullDoc.User?.Email ?? string.Empty,
            PhoneNumber = fullDoc.User?.PhoneNumber ?? string.Empty,
            SpecializationName = fullDoc.Specialization?.Name ?? string.Empty,
            LicenseNumber = fullDoc.LicenseNumber,
            ConsultationFee = fullDoc.ConsultationFee,
            RegisteredAt = fullDoc.CreatedAt
        }).OrderByDescending(d => d.RegisteredAt).ToList();

        return Result<List<DoctorApprovalSummaryDto>>.Success(resultList);
    }

    public async Task<Result> ApproveDoctorAsync(int doctorId)
    {
        var doctor = await _uow.Doctors.GetDoctorWithDetailsAsync(doctorId);
        if (doctor == null)
        {
            return Result.Failure("Doctor not found.");
        }

        if (doctor.IsApproved)
        {
            return Result.Failure("Doctor account is already approved.");
        }

        doctor.IsApproved = true;
        _uow.Doctors.Update(doctor);
        await _uow.CommitAsync();

        _logger.LogInformation("Admin approved doctor account: DoctorId={DoctorId}, Name={FullName}",
            doctor.Id, doctor.User?.FullName);

        // Send confirmation email
        if (!string.IsNullOrEmpty(doctor.User?.Email))
        {
            var subject = "MediCare — Doctor Account Approved";
            var body = $@"
                <div style='font-family: Arial, sans-serif; line-height: 1.6;'>
                    <h2>Welcome to MediCare, Dr. {doctor.User.FullName}!</h2>
                    <p>Your physician registration credentials have been verified and approved by the Clinic Administrator.</p>
                    <p>You can now log in to the doctor portal to configure your weekly schedule, manage leaves, and receive patient consultations.</p>
                    <p>Best regards,<br/><strong>MediCare Administration</strong></p>
                </div>";

            await _emailService.SendEmailAsync(doctor.User.Email, subject, body);
        }

        return Result.Success();
    }

    public async Task<Result> RejectDoctorAsync(int doctorId, string? reason)
    {
        var doctor = await _uow.Doctors.GetDoctorWithDetailsAsync(doctorId);
        if (doctor == null)
        {
            return Result.Failure("Doctor not found.");
        }

        if (doctor.IsApproved)
        {
            return Result.Failure("Cannot reject an already approved doctor.");
        }

        var doctorUser = doctor.User;
        var doctorUserId = doctor.UserId;
        var doctorEmail = doctor.User?.Email;
        var doctorName = doctor.User?.FullName;

        // Delete unapproved doctor record
        _uow.Doctors.Delete(doctor);
        await _uow.CommitAsync();

        // Delete associated orphaned ApplicationUser account so email/credentials are not locked
        if (_userManager != null && (!string.IsNullOrEmpty(doctorUserId) || doctorUser != null))
        {
            var user = doctorUser ?? await _userManager.FindByIdAsync(doctorUserId);
            if (user != null)
            {
                var userDeleteResult = await _userManager.DeleteAsync(user);
                if (!userDeleteResult.Succeeded)
                {
                    _logger.LogWarning("Failed to delete user account {UserId} for rejected doctor: {Errors}",
                        doctorUserId, string.Join(", ", userDeleteResult.Errors.Select(e => e.Description)));
                }
            }
        }

        _logger.LogInformation("Admin rejected unapproved doctor registration: DoctorId={DoctorId}, Reason={Reason}",
            doctorId, reason);

        if (!string.IsNullOrEmpty(doctorEmail))
        {
            var subject = "MediCare — Doctor Application Update";
            var rejectionReason = string.IsNullOrWhiteSpace(reason) ? "Application credentials could not be verified at this time." : reason;
            var body = $@"
                <div style='font-family: Arial, sans-serif; line-height: 1.6;'>
                    <h2>Hello Dr. {doctorName},</h2>
                    <p>Thank you for your interest in joining the MediCare medical network.</p>
                    <p>After review, your application was not approved for the following reason:</p>
                    <blockquote style='background: #f8f9fa; padding: 10px; border-left: 4px solid #dc3545;'>{rejectionReason}</blockquote>
                    <p>Please contact clinic administration for further inquiries.</p>
                </div>";

            await _emailService.SendEmailAsync(doctorEmail, subject, body);
        }

        return Result.Success();
    }

    public async Task<Result<AdminDashboardMetricsDto>> GetDashboardMetricsAsync()
    {
        var dbSummary = await _uow.Appointments.GetDashboardMetricsSummaryAsync(_clinicClock.Today);
        var dto = new AdminDashboardMetricsDto
        {
            TotalAppointments = dbSummary.TotalAppointments,
            ActiveDoctorsCount = dbSummary.ActiveDoctorsCount,
            PendingDoctorsCount = dbSummary.PendingDoctorsCount,
            CompletedVisitsCount = dbSummary.CompletedVisitsCount,
            TotalRevenueCollected = dbSummary.TotalRevenueCollected,
            TotalPendingRevenue = dbSummary.TotalPendingRevenue,
            TotalRefundedRevenue = dbSummary.TotalRefundedRevenue,
            RefundedAppointmentsCount = dbSummary.RefundedAppointmentsCount,
            MonthlyTrends = dbSummary.MonthlyTrends.Select(t => new MonthlyStatusMetricDto
            {
                MonthLabel = t.MonthLabel,
                Year = t.Year,
                Month = t.Month,
                CompletedCount = t.CompletedCount,
                CancelledCount = t.CancelledCount,
                NoShowCount = t.NoShowCount,
                RefundedCount = t.RefundedCount,
                PaidRevenue = t.PaidRevenue,
                UnpaidRevenue = t.UnpaidRevenue,
                RefundedRevenue = t.RefundedRevenue
            }).ToList(),
            SpecializationBreakdown = dbSummary.Specializations.Select(s => new SpecializationMetricDto
            {
                SpecializationName = s.SpecializationName,
                AppointmentCount = s.AppointmentCount
            }).ToList(),
            TopDoctors = dbSummary.TopDoctors.Select(d => new TopDoctorMetricDto
            {
                DoctorId = d.DoctorId,
                DoctorName = d.DoctorName,
                SpecializationName = d.SpecializationName,
                TotalAppointments = d.TotalAppointments,
                TotalRevenue = d.TotalRevenue
            }).ToList(),
            Demographics = new PatientDemographicsDto
            {
                TotalPatients = dbSummary.Demographics.TotalPatients,
                MaleCount = dbSummary.Demographics.MaleCount,
                FemaleCount = dbSummary.Demographics.FemaleCount,
                AgeUnder18Count = dbSummary.Demographics.AgeUnder18Count,
                Age18To35Count = dbSummary.Demographics.Age18To35Count,
                Age36To50Count = dbSummary.Demographics.Age36To50Count,
                AgeOver50Count = dbSummary.Demographics.AgeOver50Count
            }
        };

        return Result<AdminDashboardMetricsDto>.Success(dto);
    }

    private async Task<List<AppointmentExportRow>> GetAppointmentExportRowsAsync()
    {
        var all = (await _uow.Appointments.GetAllAsync())
            .OrderByDescending(a => a.AppointmentDate)
            .ThenByDescending(a => a.StartTime)
            .ToList();

        var allDoctors = (await _uow.Doctors.GetAllWithDetailsAsync()) ?? (await _uow.Doctors.GetAllAsync()).ToList();
        var docDict = new Dictionary<int, Doctor>();
        foreach (var d in allDoctors)
        {
            var fullDoc = d.User != null ? d : (await _uow.Doctors.GetDoctorWithDetailsAsync(d.Id) ?? d);
            if (fullDoc != null) docDict[d.Id] = fullDoc;
        }

        var allPatients = (await _uow.Patients.GetAllAsync()).ToList();
        var patDict = allPatients.ToDictionary(p => p.Id, p => p);

        var rows = new List<AppointmentExportRow>();
        foreach (var appt in all)
        {
            var docName = docDict.TryGetValue(appt.DoctorId, out var doc) ? (doc.User?.FullName ?? "Unknown") : "Unknown";
            var specName = doc != null && doc.Specialization != null ? doc.Specialization.Name : "General";
            var patName = patDict.TryGetValue(appt.PatientId, out var pat) && pat.User != null ? pat.User.FullName : $"Patient #{appt.PatientId}";

            rows.Add(new AppointmentExportRow
            {
                Id = appt.Id,
                Date = appt.AppointmentDate.ToString("yyyy-MM-dd"),
                Time = appt.StartTime.ToString(@"hh\:mm"),
                DoctorName = docName,
                Specialization = specName,
                PatientName = patName,
                Status = appt.Status.ToString(),
                Fee = appt.ConsultationFee,
                PaymentStatus = appt.PaymentStatus.ToString()
            });
        }

        return rows;
    }

    public async Task<Result<byte[]>> ExportAppointmentsCsvAsync()
    {
        var rows = await GetAppointmentExportRowsAsync();
        var sb = new StringBuilder();

        // Standard CSV Headers
        sb.AppendLine("AppointmentId,Date,Time,Doctor,Specialization,Patient,Status,Fee,PaymentStatus");

        foreach (var r in rows)
        {
            var row = string.Join(",",
                r.Id,
                r.Date,
                r.Time,
                EscapeCsv(r.DoctorName),
                EscapeCsv(r.Specialization),
                EscapeCsv(r.PatientName),
                r.Status,
                r.Fee.ToString("F2", CultureInfo.InvariantCulture),
                r.PaymentStatus
            );

            sb.AppendLine(row);
        }

        // UTF-8 with BOM for Excel compatibility
        var preamble = Encoding.UTF8.GetPreamble();
        var bodyBytes = Encoding.UTF8.GetBytes(sb.ToString());
        var result = new byte[preamble.Length + bodyBytes.Length];
        Buffer.BlockCopy(preamble, 0, result, 0, preamble.Length);
        Buffer.BlockCopy(bodyBytes, 0, result, preamble.Length, bodyBytes.Length);

        return Result<byte[]>.Success(result);
    }

    public async Task<Result<byte[]>> ExportAppointmentsExcelAsync()
    {
        var rows = await GetAppointmentExportRowsAsync();
        var bytes = ReportExportGenerators.GenerateExcel(rows);
        return Result<byte[]>.Success(bytes);
    }

    public async Task<Result<byte[]>> ExportAppointmentsPdfAsync()
    {
        var rows = await GetAppointmentExportRowsAsync();
        var bytes = ReportExportGenerators.GeneratePdf(rows);
        return Result<byte[]>.Success(bytes);
    }

    private static string EscapeCsv(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";

        // Mitigate CSV Formula Injection (CWE-1236)
        if (value.StartsWith("=") || value.StartsWith("+") || value.StartsWith("-") || value.StartsWith("@") || value.StartsWith("\t"))
        {
            value = "'" + value;
        }

        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
        {
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }
        return value;
    }


    public async Task<Result<List<AdminPatientSummaryDto>>> GetPatientsAsync(string? searchTerm = null)
    {
        var pagedResult = await GetPatientsPagedAsync(searchTerm, 1, int.MaxValue);
        if (!pagedResult.IsSuccess)
        {
            return Result<List<AdminPatientSummaryDto>>.Failure(pagedResult.Error ?? "Failed to load patient records.");
        }
        return Result<List<AdminPatientSummaryDto>>.Success(pagedResult.Value!.Items);
    }

    public async Task<Result<PagedResult<AdminPatientSummaryDto>>> GetPatientsPagedAsync(string? searchTerm = null, int page = 1, int pageSize = 20)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;
        if (pageSize > 100 && pageSize != int.MaxValue) pageSize = 100;

        var patients = (await _uow.Patients.GetAllAsync()).ToList();

        Dictionary<string, ApplicationUser>? users = null;
        if (_userManager != null)
        {
            try
            {
                users = _userManager.Users.ToDictionary(u => u.Id, u => u);
            }
            catch
            {
                users = null;
            }
        }

        var filteredPatients = new List<(Patient Patient, string FullName, string Email, string? Phone, bool IsLockedOut)>();
        var term = searchTerm?.Trim();
        var hasSearch = !string.IsNullOrWhiteSpace(term);

        foreach (var p in patients)
        {
            ApplicationUser? user = null;
            users?.TryGetValue(p.UserId, out user);

            var fullName = user?.FullName ?? p.User?.FullName ?? $"Patient #{p.Id}";
            var email = user?.Email ?? p.User?.Email ?? string.Empty;
            var phone = user?.PhoneNumber ?? p.User?.PhoneNumber;
            var isLockedOut = user != null && user.LockoutEnd.HasValue && user.LockoutEnd.Value > DateTimeOffset.UtcNow;

            if (hasSearch)
            {
                bool matches = fullName.Contains(term!, StringComparison.OrdinalIgnoreCase) ||
                               email.Contains(term!, StringComparison.OrdinalIgnoreCase) ||
                               (phone != null && phone.Contains(term!, StringComparison.OrdinalIgnoreCase));
                if (!matches) continue;
            }

            filteredPatients.Add((p, fullName, email, phone, isLockedOut));
        }

        // Maintain consistent alphabetical ordering by FullName
        filteredPatients = filteredPatients.OrderBy(p => p.FullName).ToList();
        var totalCount = filteredPatients.Count;

        var pagedItems = (pageSize == int.MaxValue)
            ? filteredPatients
            : filteredPatients.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        var pagedPatientIds = pagedItems.Select(x => x.Patient.Id).ToList();

        // Optimized appointment count: fetch counts only for patients on this page
        Dictionary<int, int> apptCounts = null!;
        try
        {
            apptCounts = await _uow.Appointments.GetAppointmentCountsByPatientAsync(pagedPatientIds);
        }
        catch
        {
            apptCounts = null!;
        }

        if (apptCounts == null)
        {
            // Defensive fallback for unit test mocks where GetAppointmentCountsByPatientAsync is unmocked
            var allAppointments = (await _uow.Appointments.GetAllAsync()).ToList();
            apptCounts = allAppointments
                .GroupBy(a => a.PatientId)
                .ToDictionary(g => g.Key, g => g.Count());
        }

        var resultList = new List<AdminPatientSummaryDto>(pagedItems.Count);
        foreach (var item in pagedItems)
        {
            var p = item.Patient;
            var count = apptCounts.TryGetValue(p.Id, out var c) ? c : 0;

            resultList.Add(new AdminPatientSummaryDto
            {
                Id = p.Id,
                UserId = p.UserId,
                FullName = item.FullName,
                Email = item.Email,
                PhoneNumber = item.Phone,
                DateOfBirth = p.DateOfBirth,
                Gender = p.Gender,
                BloodGroup = p.BloodGroup,
                EmergencyContact = p.EmergencyContact,
                Allergies = p.Allergies,
                MedicalHistory = p.MedicalHistory,
                IsLockedOut = item.IsLockedOut,
                CreatedAt = p.CreatedAt,
                TotalAppointments = count
            });
        }

        var pagedResult = new PagedResult<AdminPatientSummaryDto>(
            resultList,
            totalCount,
            page,
            pageSize == int.MaxValue ? (totalCount > 0 ? totalCount : 1) : pageSize);

        return Result<PagedResult<AdminPatientSummaryDto>>.Success(pagedResult);
    }

    public async Task<Result> TogglePatientLockoutAsync(int patientId, bool lockout)
    {
        var patient = await _uow.Patients.GetByIdAsync(patientId);
        if (patient == null)
        {
            return Result.Failure("Patient record not found.");
        }

        if (_userManager != null)
        {
            var user = await _userManager.FindByIdAsync(patient.UserId);
            if (user == null)
            {
                return Result.Failure("Patient user account not found.");
            }

            if (lockout)
            {
                await _userManager.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddYears(100));
            }
            else
            {
                await _userManager.SetLockoutEndDateAsync(user, null);
                await _userManager.ResetAccessFailedCountAsync(user);
            }

            _logger.LogInformation("Admin toggled lockout for patient {PatientId} (User {UserId}): Lockout={Lockout}",
                patientId, patient.UserId, lockout);
        }

        return Result.Success();
    }

    public async Task<Result> CreatePharmacistAsync(CreatePharmacistDto dto, string createdByAdminId)
    {
        if (string.IsNullOrWhiteSpace(dto.FullName) || string.IsNullOrWhiteSpace(dto.Email) || string.IsNullOrWhiteSpace(dto.Password))
        {
            return Result.Failure("All pharmacist details (Full Name, Email, Password) are required.");
        }

        var trimmedName = dto.FullName.Trim();
        if (trimmedName.Length < 3 || trimmedName.Any(char.IsDigit) || !System.Text.RegularExpressions.Regex.IsMatch(trimmedName, @"^[a-zA-Z\u0621-\u064A\u0671-\u06D3\u064B-\u065F\s.'\-]+$"))
        {
            return Result.Failure("Pharmacist Full Name must contain only letters (Arabic or English) and cannot contain numbers.");
        }

        if (_userManager == null)
        {
            return Result.Failure("User management is currently unavailable.");
        }

        var existingUser = await _userManager.FindByEmailAsync(dto.Email.Trim());
        if (existingUser != null)
        {
            return Result.Failure($"A user with email '{dto.Email.Trim()}' already exists.");
        }

        var pharmacistUser = new ApplicationUser
        {
            UserName = dto.Email.Trim(),
            Email = dto.Email.Trim(),
            FullName = dto.FullName.Trim(),
            EmailConfirmed = true,
            CreatedAt = _clinicClock.Now
        };

        var createResult = await _userManager.CreateAsync(pharmacistUser, dto.Password);
        if (!createResult.Succeeded)
        {
            var errors = string.Join("; ", createResult.Errors.Select(e => e.Description));
            return Result.Failure($"Password requirements failed: {errors}");
        }

        await _userManager.AddToRoleAsync(pharmacistUser, "Pharmacist");
        await _userManager.AddClaimAsync(pharmacistUser, new System.Security.Claims.Claim("MustChangePassword", "true"));

        _logger.LogInformation("Admin {AdminId} successfully created Pharmacist account for {Email} (UserId {UserId}) with mandatory first-login password change",
            createdByAdminId, pharmacistUser.Email, pharmacistUser.Id);

        return Result.Success();
    }

    public async Task<Result<List<PharmacistSummaryDto>>> GetPharmacistsAsync()
    {
        if (_userManager == null)
        {
            return Result<List<PharmacistSummaryDto>>.Failure("User manager is not available.");
        }

        var pharmacists = await _userManager.GetUsersInRoleAsync("Pharmacist");
        var now = DateTimeOffset.UtcNow;
        var dtos = pharmacists.Select(u => new PharmacistSummaryDto
        {
            Id = u.Id,
            FullName = u.FullName,
            Email = u.Email ?? string.Empty,
            PhoneNumber = u.PhoneNumber,
            CreatedAt = u.CreatedAt,
            IsLockedOut = u.LockoutEnd.HasValue && u.LockoutEnd.Value > now
        }).OrderByDescending(u => u.CreatedAt).ToList();

        return Result<List<PharmacistSummaryDto>>.Success(dtos);
    }

    public async Task<Result<List<SpecializationDto>>> GetAllSpecializationsAsync()
    {
        var specs = await _uow.Specializations.GetAllAsync();
        var dtos = specs.Select(s => new SpecializationDto
        {
            Id = s.Id,
            Name = s.Name,
            Description = s.Description
        }).OrderBy(s => s.Name).ToList();

        return Result<List<SpecializationDto>>.Success(dtos);
    }

    public async Task<Result<SpecializationDto>> GetSpecializationByIdAsync(int id)
    {
        var spec = await _uow.Specializations.GetByIdAsync(id);
        if (spec == null)
        {
            return Result<SpecializationDto>.Failure("Specialization not found.");
        }

        return Result<SpecializationDto>.Success(new SpecializationDto
        {
            Id = spec.Id,
            Name = spec.Name,
            Description = spec.Description
        });
    }

    public async Task<Result<int>> CreateSpecializationAsync(CreateSpecializationDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
        {
            return Result<int>.Failure("Specialization name is required.");
        }

        var normalizedName = dto.Name.Trim();
        var existing = await _uow.Specializations.FindAsync(s => s.Name.ToLower() == normalizedName.ToLower());
        if (existing.Any())
        {
            return Result<int>.Failure($"A specialization named '{normalizedName}' already exists.");
        }

        var entity = new Specialization
        {
            Name = normalizedName,
            Description = dto.Description?.Trim()
        };

        await _uow.Specializations.AddAsync(entity);
        await _uow.CommitAsync();

        _logger.LogInformation("Admin created specialization {SpecializationName} with Id {SpecializationId}", entity.Name, entity.Id);
        return Result<int>.Success(entity.Id);
    }

    public async Task<Result> UpdateSpecializationAsync(int id, UpdateSpecializationDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
        {
            return Result.Failure("Specialization name is required.");
        }

        var spec = await _uow.Specializations.GetByIdAsync(id);
        if (spec == null)
        {
            return Result.Failure("Specialization not found.");
        }

        var normalizedName = dto.Name.Trim();
        var existing = await _uow.Specializations.FindAsync(s => s.Id != id && s.Name.ToLower() == normalizedName.ToLower());
        if (existing.Any())
        {
            return Result.Failure($"Another specialization named '{normalizedName}' already exists.");
        }

        spec.Name = normalizedName;
        spec.Description = dto.Description?.Trim();
        spec.UpdatedAt = _clinicClock.Now;

        _uow.Specializations.Update(spec);
        await _uow.CommitAsync();

        _logger.LogInformation("Admin updated specialization {SpecializationId} to {SpecializationName}", id, spec.Name);
        return Result.Success();
    }

    public async Task<Result> DeleteSpecializationAsync(int id)
    {
        var spec = await _uow.Specializations.GetByIdAsync(id);
        if (spec == null)
        {
            return Result.Failure("Specialization not found.");
        }

        var doctors = await _uow.Doctors.FindAsync(d => d.SpecializationId == id);
        if (doctors.Any())
        {
            return Result.Failure($"Cannot delete specialization '{spec.Name}' because {doctors.Count()} doctor(s) are associated with it.");
        }

        _uow.Specializations.Delete(spec);
        await _uow.CommitAsync();

        _logger.LogInformation("Admin deleted specialization {SpecializationId} ({SpecializationName})", id, spec.Name);
        return Result.Success();
    }
}
