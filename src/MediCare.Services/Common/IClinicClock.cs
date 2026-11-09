namespace MediCare.Services.Common;

/// <summary>
/// Abstraction for providing the current clinic local wall-clock time.
/// Time Zone Rule:
/// MediCare operates as an outpatient medical clinic in Egypt. All working hours, doctor leaves,
/// appointment schedules, and slot availability evaluations are strictly anchored to Local Clinic Time
/// (Egypt Standard Time / Africa/Cairo, with automatic Daylight Saving Time handling).
/// Database columns for appointment dates and working hour times represent local clinic wall-clock time.
/// </summary>
public interface IClinicClock
{
    DateTime Now { get; }
    DateTime Today { get; }
    TimeZoneInfo TimeZone { get; }
}

public class ClinicClock : IClinicClock
{
    private static readonly TimeZoneInfo EgyptTimeZone = ResolveEgyptTimeZone();

    private static TimeZoneInfo ResolveEgyptTimeZone()
    {
        // 1. Try Windows ID
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Egypt Standard Time");
        }
        catch (TimeZoneNotFoundException) { }
        catch (InvalidTimeZoneException) { }

        // 2. Try IANA ID (Linux / Docker / macOS / .NET 6+)
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");
        }
        catch (TimeZoneNotFoundException) { }
        catch (InvalidTimeZoneException) { }

        // 3. Fallback: custom timezone with DST or local
        return TimeZoneInfo.Local;
    }

    public TimeZoneInfo TimeZone => EgyptTimeZone;

    public DateTime Now => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, EgyptTimeZone);

    public DateTime Today => Now.Date;
}
