using System.Numerics;

namespace Beutl.Validation;

internal sealed class NumberRangeValidator<TNumber> : RangeValidator<TNumber>
    where TNumber : struct, INumber<TNumber>, IMinMaxValue<TNumber>
{
    public NumberRangeValidator()
    {
        Maximum = TNumber.MaxValue;
        Minimum = TNumber.MinValue;
    }

    public override bool TryCoerce(ValidationContext context, ref TNumber value)
    {
        value = TNumber.Clamp(value, Minimum, Maximum);
        return true;
    }

    public override string? Validate(ValidationContext context, TNumber value)
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
