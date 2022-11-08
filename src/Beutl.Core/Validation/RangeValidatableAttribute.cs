namespace Beutl.Validation;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false, AllowMultiple = false)]
public sealed class RangeValidatableAttribute : Attribute
{
    public RangeValidatableAttribute(Type validatorType)
    {
        if (validatorType.BaseType is { } baseType
            && baseType.GetGenericArguments().FirstOrDefault() is Type targetType
            && typeof(RangeValidator<>).MakeGenericType(targetType).IsAssignableFrom(validatorType)
            && !validatorType.IsAbstract)
        {
            ValidatorType = validatorType;
            TargetType = targetType;
        }
        else
        {
            throw new InvalidOperationException();
        }
    }

    public Type ValidatorType { get; }

    public Type TargetType { get; }
}
