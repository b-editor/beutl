namespace Beutl.Editor.Components.VersionControlTab.ViewModels;

internal sealed class VersionControlRelativeTimeFormatter
{
    private readonly TimeProvider _timeProvider;

    public VersionControlRelativeTimeFormatter(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    public string Format(DateTimeOffset timestamp)
    {
        TimeSpan elapsed = _timeProvider.GetUtcNow() - timestamp.ToUniversalTime();
        if (elapsed < TimeSpan.FromMinutes(1))
        {
            return Strings.VersionControl_TimeJustNow;
        }

        int minutes = (int)Math.Floor(elapsed.TotalMinutes);
        if (minutes < 60)
        {
            return minutes == 1
                ? Strings.VersionControl_TimeMinuteAgo
                : FormatCount(Strings.VersionControl_TimeMinutesAgoFormat, minutes);
        }

        int hours = (int)Math.Floor(elapsed.TotalHours);
        if (hours < 24)
        {
            return hours == 1
                ? Strings.VersionControl_TimeHourAgo
                : FormatCount(Strings.VersionControl_TimeHoursAgoFormat, hours);
        }

        int days = (int)Math.Floor(elapsed.TotalDays);
        return days == 1
            ? Strings.VersionControl_TimeDayAgo
            : FormatCount(Strings.VersionControl_TimeDaysAgoFormat, days);
    }

    public string FormatAbsoluteLocal(DateTimeOffset timestamp)
    {
        return TimeZoneInfo.ConvertTime(timestamp, _timeProvider.LocalTimeZone)
            .ToString("g", CultureInfo.CurrentUICulture);
    }

    private static string FormatCount(string format, int value)
    {
        return string.Format(CultureInfo.CurrentUICulture, format, value);
    }
}
