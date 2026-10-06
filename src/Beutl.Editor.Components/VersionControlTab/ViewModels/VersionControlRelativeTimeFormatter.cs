using System.Globalization;
using System.Resources;

namespace Beutl.Editor.Components.VersionControlTab.ViewModels;

internal sealed class VersionControlRelativeTimeFormatter
{
    private static readonly ResourceManager s_resourceManager =
        new("Beutl.Language.Strings", typeof(Strings).Assembly);

    private readonly TimeProvider _timeProvider;
    private readonly CultureInfo _culture;

    public VersionControlRelativeTimeFormatter(
        TimeProvider timeProvider,
        CultureInfo culture)
    {
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _culture = culture ?? throw new ArgumentNullException(nameof(culture));
    }

    public string Format(DateTimeOffset timestamp)
    {
        TimeSpan elapsed = _timeProvider.GetUtcNow() - timestamp.ToUniversalTime();
        if (elapsed < TimeSpan.FromMinutes(1))
        {
            return GetString("VersionControl_TimeJustNow");
        }

        int minutes = (int)Math.Floor(elapsed.TotalMinutes);
        if (minutes < 60)
        {
            return minutes == 1
                ? GetString("VersionControl_TimeMinuteAgo")
                : FormatCount("VersionControl_TimeMinutesAgoFormat", minutes);
        }

        int hours = (int)Math.Floor(elapsed.TotalHours);
        if (hours < 24)
        {
            return hours == 1
                ? GetString("VersionControl_TimeHourAgo")
                : FormatCount("VersionControl_TimeHoursAgoFormat", hours);
        }

        int days = (int)Math.Floor(elapsed.TotalDays);
        return days == 1
            ? GetString("VersionControl_TimeDayAgo")
            : FormatCount("VersionControl_TimeDaysAgoFormat", days);
    }

    public string FormatAbsoluteLocal(DateTimeOffset timestamp)
    {
        return TimeZoneInfo.ConvertTime(timestamp, _timeProvider.LocalTimeZone)
            .ToString("g", _culture);
    }

    private string FormatCount(string key, int value)
    {
        return string.Format(_culture, GetString(key), value);
    }

    private string GetString(string key)
    {
        return s_resourceManager.GetString(key, _culture)
               ?? throw new MissingManifestResourceException(
                   $"The localized resource '{key}' is missing.");
    }
}
