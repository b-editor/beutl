namespace Beutl.Services.AI;

internal static class RelativeTimeText
{
    /// <summary>
    /// How long ago something happened, worded the way a person looking for the
    /// one they just made would ask for it. Past a week the calendar date is what
    /// identifies it, so that is what comes back.
    /// </summary>
    public static string Format(DateTimeOffset moment, DateTimeOffset now)
    {
        TimeSpan elapsed = now - moment;
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        if (elapsed < TimeSpan.FromMinutes(1))
            return Strings.TimeAgoJustNow;
        if (elapsed < TimeSpan.FromHours(1))
            return string.Format(Strings.TimeAgoMinutesFormat, (int)elapsed.TotalMinutes);
        if (elapsed < TimeSpan.FromDays(1))
            return string.Format(Strings.TimeAgoHoursFormat, (int)elapsed.TotalHours);
        if (elapsed < TimeSpan.FromDays(7))
            return string.Format(Strings.TimeAgoDaysFormat, (int)elapsed.TotalDays);

        return moment.ToLocalTime().ToString("d");
    }
}
