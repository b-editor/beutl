using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Numerics;

namespace Beutl.Validation;

public sealed class RangeDataAnnotationValidater<TNumber> : RangeValidator<TNumber>
    where TNumber : struct, INumber<TNumber>, IMinMaxValue<TNumber>
{
    public RangeDataAnnotationValidater()
    {
    }

    public RangeDataAnnotationValidater(RangeAttribute attribute)
    {
        Attribute = attribute;
        if (Attribute.OperandType == typeof(double))
        {
            Maximum = TNumber.CreateTruncating((double)attribute.Maximum);
            Minimum = TNumber.CreateTruncating((double)attribute.Minimum);
        }
        else if (Attribute.OperandType == typeof(int))
        {
            Maximum = TNumber.CreateTruncating((int)attribute.Maximum);
            Minimum = TNumber.CreateTruncating((int)attribute.Minimum);
        }
        else if (Attribute.OperandType == typeof(TNumber)
            && Attribute.Maximum is string maximumStr
            && Attribute.Minimum is string minimumStr)
        {
            TypeConverter converter = TypeDescriptor.GetConverter(Attribute.OperandType);
            Maximum = (TNumber)converter.ConvertFromInvariantString(maximumStr)!;
            Minimum = (TNumber)converter.ConvertFromInvariantString(minimumStr)!;
        }
    }

    public RangeAttribute? Attribute { get; set; }

    public override bool TryCoerce(ValidationContext context, ref TNumber value)
    {
        switch ((Attribute?.MaximumIsExclusive, Attribute?.MinimumIsExclusive))
        {
            case (true, true):
                return false;

            case (true, false):
                value = TNumber.Max(value, Minimum);
                return true;

            case (false, true):
                value = TNumber.Min(value, Maximum);
                return true;

            case (false, false):
                value = TNumber.Clamp(value, Minimum, Maximum);
                value = TNumber.Min(value, Maximum);
                return true;

            default:
                return false;
        }
    }

    public override string? Validate(ValidationContext context, TNumber value)
    {
        if (Attribute == null)
        {
            return null;
        }

        if (!Attribute.RequiresValidationContext)
        {
            if (!Attribute.IsValid(value))
            {
                return Attribute.FormatErrorMessage(context.Property?.Name ?? typeof(TNumber).Name);
            }
            else
            {
                return null;
            }
        }
        else
        {
            throw new InvalidOperationException("System.ComponentModel.DataAnnotations.ValidationContext required validation is not yet supported.");
        }
    }
}
