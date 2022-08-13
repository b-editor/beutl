namespace BeUtl.Validation;

internal sealed class TimeSpanRangeValidator : RangeValidator<TimeSpan>
{
    public TimeSpanRangeValidator()
    {
        Maximum = TimeSpan.MaxValue;
        Minimum = TimeSpan.MinValue;
    }

    public override TimeSpan Coerce(ICoreObject? obj, TimeSpan value)
    {
        return TimeSpan.FromTicks(Math.Clamp(value.Ticks, Minimum.Ticks, Maximum.Ticks));
    }

    public override bool Validate(ICoreObject? obj, TimeSpan value)
    {
        return value >= Minimum && value <= Maximum;
    }
}
