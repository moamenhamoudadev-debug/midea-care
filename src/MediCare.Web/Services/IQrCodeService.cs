namespace MediCare.Web.Services;

public interface IQrCodeService
{
    string GeneratePngDataUri(string payload, int pixelsPerModule = 4);
}
