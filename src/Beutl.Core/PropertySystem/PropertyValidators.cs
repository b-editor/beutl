using System.ComponentModel.DataAnnotations;
using System.Numerics;
using Beutl.Validation;

namespace Beutl;

internal static class PropertyValidators
{
    public static IValidator<T>[] FromAttributes<T>(IEnumerable<ValidationAttribute> attributes)
    {
        return attributes
            .Select(ConvertValidator<T>)
            .ToArray();
    }

    private static IValidator<T> ConvertValidator<T>(ValidationAttribute att)
    {
        switch (att)
        {
            case RangeAttribute rangeAttribute:
                Type propType = typeof(T);
                Type[] interfaces = propType.GetInterfaces();
                if (propType.IsValueType)
                {
                    if (interfaces.Any(x => x.IsGenericType && x.GetGenericTypeDefinition() == typeof(INumber<>))
                        && interfaces.Any(x => x.IsGenericType && x.GetGenericTypeDefinition() == typeof(IMinMaxValue<>)))
                    {
                        Type validatorType = typeof(RangeDataAnnotationValidater<>).MakeGenericType(propType);
                        if (Activator.CreateInstance(validatorType, rangeAttribute) is IValidator<T> validator)
                        {
                            return validator;
                        }
                    }
                    else if (interfaces.FirstOrDefault(x => x.IsGenericType && x.GetGenericTypeDefinition() == typeof(ITupleConvertible<,>)) is { } interfaceType)
                    {
                        Type validatorType = typeof(TupleRangeDataAnnotationValidater<,>);
                        validatorType = validatorType.MakeGenericType(interfaceType.GetGenericArguments());
                        if (Activator.CreateInstance(validatorType, rangeAttribute) is IValidator<T> validator)
                        {
                            return validator;
                        }
                    }
                }

                goto default;

            case DataTypeAttribute dataTypeAttribute:
                if (dataTypeAttribute.DataType == DataType.MultilineText)
                {
                    // MultilineTextの場合はバリデーションを行わない
                    return new DataAnnotationValidater<T>(null);
                }
                goto default;

            default:
                return new DataAnnotationValidater<T>(att);
        }
    }
}
