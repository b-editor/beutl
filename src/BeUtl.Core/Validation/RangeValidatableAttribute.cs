namespace BeUtl.Validation;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false, AllowMultiple = false)]
public sealed class RangeValidatableAttribute : Attribute
{
    public RangeValidatableAttribute(Type validatorType)
    {
        if (typeof(RangeValidator<>).MakeGenericType(validatorType).IsAssignableFrom(validatorType) && !validatorType.IsAbstract)
        {
            ValidatorType = validatorType;
        }
        else
        {
            throw new InvalidOperationException();
        }
    }

    public Type ValidatorType { get; }
}
