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

    public override TNumber Coerce(ICoreObject? obj, TNumber value)
    {
        return TNumber.Clamp(value, Minimum, Maximum);
    }

    public override bool Validate(ICoreObject? obj, TNumber value)
    {
        return value >= Minimum && value <= Maximum;
    }
}
