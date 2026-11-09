using MediCare.Services.Common;
using MediCare.Services.DTOs;
using Microsoft.AspNetCore.Http;

namespace MediCare.Services.Contracts;

public interface IMedicalRecordService
{
    Task<Result<int>> SaveEncounterAsync(CreateEncounterDto dto, IFormFile? attachment, string doctorUserId, string webRootPath);
    Task<Result<MedicalRecordDetailsDto>> GetRecordDetailsAsync(int recordId, string userId, bool isDoctor, bool isPatient, bool isAdmin);
    Task<Result<List<MedicalRecordTimelineDto>>> GetPatientTimelineAsync(string patientUserId, string requestingUserId, bool isDoctor, bool isAdmin);
    Task<Result<AppointmentSummaryDto>> ValidateEncounterAccessAsync(int appointmentId, string doctorUserId);
    Task<Result<string>> UploadPatientAttachmentAsync(int appointmentId, IFormFile file, string requestingUserId, string webRootPath);
}
