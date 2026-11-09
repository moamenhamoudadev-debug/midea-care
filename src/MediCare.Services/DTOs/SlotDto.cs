namespace MediCare.Services.DTOs;

public class SlotDto
{
    public DateTime Date { get; set; }
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }
    public string FormattedTime { get; set; } = string.Empty;
    public bool IsAvailable { get; set; }
}
