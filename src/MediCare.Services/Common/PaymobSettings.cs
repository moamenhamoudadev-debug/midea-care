namespace MediCare.Services.Common;

public sealed class PaymobSettings
{
    public const string SectionName = "Paymob";
    public string SecretKey { get; set; } = string.Empty;
    public string PublicKey { get; set; } = string.Empty;
    public string HmacSecret { get; set; } = string.Empty;
    public string IntegrationId { get; set; } = string.Empty;
    public string? WalletIntegrationId { get; set; }
    public bool EnableMobileWallets { get; set; } = false;
    public string PublicBaseUrl { get; set; } = string.Empty;
    public decimal MakerCheckerThresholdEgp { get; set; } = 500m;
}
