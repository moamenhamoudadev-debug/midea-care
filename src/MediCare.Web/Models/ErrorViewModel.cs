namespace MediCare.Web.Models;

public class ErrorViewModel
{
    public string? RequestId { get; set; }

    public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);

    public int? StatusCode { get; set; }

    public string? OriginalPath { get; set; }

    public bool IsNotFound => StatusCode == 404;
}
