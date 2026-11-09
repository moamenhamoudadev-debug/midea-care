namespace MediCare.Web.ViewModels;

public class TelehealthUnavailableViewModel
{
    public int AppointmentId { get; set; }
    public string Title { get; set; } = "Telehealth Session Unavailable";
    public string Message { get; set; } = string.Empty;
    public DateTime? WindowOpensAt { get; set; }
    public DateTime? WindowClosesAt { get; set; }
}
