using MediCare.Services.Common;
using MediCare.Services.DTOs;

namespace MediCare.Services.Contracts;

public interface IAuthService
{
    Task<Result<string>> RegisterPatientAsync(PatientRegisterDto dto);
    Task<Result<string>> RegisterDoctorAsync(DoctorRegisterDto dto);
    Task<Result> LoginAsync(LoginDto dto);
    Task LogoutAsync();
    Task<Result<PatientProfileDto>> GetPatientProfileAsync(string userId);
    Task<Result> UpdatePatientProfileAsync(string userId, PatientUpdateProfileDto dto);
    Task<Result> ChangePasswordAsync(string userId, string currentPassword, string newPassword);
}
