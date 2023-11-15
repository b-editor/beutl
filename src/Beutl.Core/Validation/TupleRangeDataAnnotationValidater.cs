using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Numerics;

namespace Beutl.Validation;

public sealed class TupleRangeDataAnnotationValidater<TTuple, TNumber> : RangeValidator<TTuple>
    where TTuple : struct, ITupleConvertible<TTuple, TNumber>
    where TNumber : unmanaged, INumber<TNumber>
{
    public TupleRangeDataAnnotationValidater(RangeAttribute attribute)
    {
        if (attribute.MaximumIsExclusive || attribute.MinimumIsExclusive)
            throw new NotSupportedException("'MaximumIsExclusive' or 'MinimumIsExclusive' cannot be set to True.");

        Attribute = attribute;
        if (Attribute.OperandType == typeof(TTuple)
            && Attribute.Maximum is string maximumStr
            && Attribute.Minimum is string minimumStr)
        {
            TypeConverter converter = TypeDescriptor.GetConverter(Attribute.OperandType);
            Maximum = (TTuple)converter.ConvertFromInvariantString(maximumStr)!;
            Minimum = (TTuple)converter.ConvertFromInvariantString(minimumStr)!;
        }
    }

    public RangeAttribute? Attribute { get; set; }

    public override bool TryCoerce(ValidationContext context, ref TTuple value)
    {
        int length = TTuple.TupleLength;
        Span<TNumber> max = stackalloc TNumber[length];
        Span<TNumber> min = stackalloc TNumber[length];
        Span<TNumber> tuple = stackalloc TNumber[length];

        TTuple.ConvertTo(Maximum, max);
        TTuple.ConvertTo(Minimum, min);
        TTuple.ConvertTo(value, tuple);

        for (int i = 0; i < length; i++)
        {
            tuple[i] = TNumber.Clamp(tuple[i], min[i], max[i]);
        }

        TTuple.ConvertFrom(tuple, out value);

        return true;
    }

    public override string? Validate(ValidationContext context, TTuple value)
    {
        return null;
    }
}
