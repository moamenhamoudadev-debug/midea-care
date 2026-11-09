using MediCare.Services.DTOs;
using Microsoft.AspNetCore.Http;

namespace MediCare.Web.ViewModels;

public class CreateEncounterViewModel
{
    public AppointmentSummaryDto Appointment { get; set; } = new();
    public CreateEncounterDto Encounter { get; set; } = new();
    public IFormFile? Attachment { get; set; }
}
