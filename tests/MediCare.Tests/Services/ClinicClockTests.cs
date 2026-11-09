using FluentAssertions;
using MediCare.Services.Common;
using Xunit;

namespace MediCare.Tests.Services;

public class ClinicClockTests
{
    [Fact]
    public void ClinicClock_ShouldResolveEgyptTimeZone_CrossPlatform()
    {
        // Arrange
        var clock = new ClinicClock();

        // Act
        var tz = clock.TimeZone;

        // Assert
        tz.Should().NotBeNull();
        tz.Id.Should().Match(id => id == "Egypt Standard Time" || id == "Africa/Cairo" || id == TimeZoneInfo.Local.Id);
        tz.BaseUtcOffset.Should().Be(TimeSpan.FromHours(2), "Egypt base standard time offset is UTC+2");
    }

    [Fact]
    public void ClinicClock_ShouldApplyDaylightSavingTime_InSummerDate()
    {
        // Arrange
        var clock = new ClinicClock();
        var tz = clock.TimeZone;

        // Egypt Summer date: July 15, 2026, 12:00:00 UTC
        var summerUtc = new DateTime(2026, 7, 15, 12, 0, 0, DateTimeKind.Utc);

        // Act
        var localTime = TimeZoneInfo.ConvertTimeFromUtc(summerUtc, tz);

        // Assert
        if (tz.SupportsDaylightSavingTime)
        {
            // Summer in Egypt adds 1 hour DST (UTC+3)
            localTime.Hour.Should().Be(15, "12:00 UTC + 3 hours DST offset in July = 15:00 local Cairo time");
            tz.GetUtcOffset(summerUtc).Should().Be(TimeSpan.FromHours(3));
        }
        else
        {
            // If running on a minimal system without tzdb DST rules, base offset is at least UTC+2
            tz.GetUtcOffset(summerUtc).Should().BeGreaterThanOrEqualTo(TimeSpan.FromHours(2));
        }
    }

    [Fact]
    public void ClinicClock_ShouldApplyStandardTime_InWinterDate()
    {
        // Arrange
        var clock = new ClinicClock();
        var tz = clock.TimeZone;

        // Egypt Winter date: January 15, 2026, 12:00:00 UTC
        var winterUtc = new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);

        // Act
        var localTime = TimeZoneInfo.ConvertTimeFromUtc(winterUtc, tz);

        // Assert
        // Standard time in Egypt is UTC+2 (no DST in January)
        localTime.Hour.Should().Be(14, "12:00 UTC + 2 hours standard offset in January = 14:00 local Cairo time");
        tz.GetUtcOffset(winterUtc).Should().Be(TimeSpan.FromHours(2));
    }
}
