using QRCoder;

namespace MediCare.Web.Services;

public class QrCodeService : IQrCodeService
{
    public string GeneratePngDataUri(string payload, int pixelsPerModule = 4)
    {
        if (string.IsNullOrEmpty(payload))
        {
            return string.Empty;
        }

        using var qrGenerator = new QRCodeGenerator();
        using var qrCodeData = qrGenerator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.M);
        var pngByteQrCode = new PngByteQRCode(qrCodeData);
        var qrCodeBytes = pngByteQrCode.GetGraphic(pixelsPerModule);

        var base64 = Convert.ToBase64String(qrCodeBytes);
        return $"data:image/png;base64,{base64}";
    }
}
