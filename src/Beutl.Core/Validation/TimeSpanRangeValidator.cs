namespace Beutl.Validation;

internal sealed class TimeSpanRangeValidator : RangeValidator<TimeSpan>
{
    public TimeSpanRangeValidator()
    {
        Maximum = TimeSpan.MaxValue;
        Minimum = TimeSpan.MinValue;
    }

    public override bool TryCoerce(ValidationContext context, ref TimeSpan value)
    {
        value = TimeSpan.FromTicks(Math.Clamp(value.Ticks, Minimum.Ticks, Maximum.Ticks));
        return true;
    }

    public override string? Validate(ValidationContext context, TimeSpan value)
    {
        if (value >= Minimum && value <= Maximum)
        {
            return $"The value must be between {Minimum} and {Maximum}.";
        }
        else
        {
            return null;
        }
    }
}
