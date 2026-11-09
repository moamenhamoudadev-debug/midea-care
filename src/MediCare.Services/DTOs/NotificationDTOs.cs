namespace MediCare.Services.DTOs;

public class NotificationDto
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Type { get; set; } = "General";
    public int? PaymentAttemptId { get; set; }
    public int? RefundId { get; set; }
    public int? AppointmentId { get; set; }
    public string? EventReference { get; set; }
    public string? ActionUrl { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ReadAtUtc { get; set; }
    public bool IsRead { get; set; }
}

public class NotificationFilterDto
{
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 10;
    public string? Type { get; set; }
    public bool? IsRead { get; set; }
}

public class NotificationPagedResultDto
{
    public List<NotificationDto> Items { get; set; } = new();
    public int TotalItems { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 10;
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling((double)TotalItems / PageSize) : 0;
    public int UnreadCount { get; set; }
    public bool HasPreviousPage => PageNumber > 1;
    public bool HasNextPage => PageNumber < TotalPages;
}

public class CreateNotificationRequestDto
{
    public string UserId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Type { get; set; } = "General";
    public int? PaymentAttemptId { get; set; }
    public int? RefundId { get; set; }
    public int? AppointmentId { get; set; }
    public string? EventReference { get; set; }
    public string? ActionUrl { get; set; }
    public bool SendEmail { get; set; } = false;
    public string? RecipientEmail { get; set; }
    public string? EmailSubject { get; set; }
}
