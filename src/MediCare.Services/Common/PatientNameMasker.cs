namespace MediCare.Services.Common;

public static class PatientNameMasker
{
    private const string StarMask = "****";

    public static string Mask(string? fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName))
        {
            return StarMask;
        }

        var parts = fullName
            .Trim()
            .Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length == 0)
        {
            return StarMask;
        }

        // Each name part keeps only its first character followed by a fixed number of stars
        // This guarantees that character length of individual name components is not leaked.
        var maskedParts = parts.Select(part =>
        {
            if (string.IsNullOrEmpty(part)) return StarMask;
            return $"{part[0]}{StarMask}";
        });

        return string.Join(" ", maskedParts);
    }
}
