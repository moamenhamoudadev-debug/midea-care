using System.Security.Cryptography;
using MediCare.Data.Entities;
using MediCare.Data.Enums;
using MediCare.Services.DTOs;

namespace MediCare.Services.Factories;

public class AppointmentFactory
{
    public static string GenerateTelehealthRoomSecret()
    {
        var bytes = RandomNumberGenerator.GetBytes(16);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public Appointment Create(BookingRequestDto dto, decimal fee, int slotDurationMinutes = 30)
    {
        var duration = slotDurationMinutes > 0 ? slotDurationMinutes : 30;
        return new Appointment
        {
            DoctorId = dto.DoctorId,
            PatientId = dto.PatientId,
            AppointmentDate = dto.AppointmentDate.Date,
            StartTime = dto.StartTime,
            EndTime = dto.StartTime.Add(TimeSpan.FromMinutes(duration)),
            Status = AppointmentStatus.Pending,
            ConsultationFee = fee,
            PaymentStatus = PaymentStatus.Unpaid,
            Type = dto.Type,
            Notes = dto.Notes?.Trim(),
            TelehealthRoomSecret = dto.Type == AppointmentType.Telemedicine ? GenerateTelehealthRoomSecret() : null
        };
    }
}
