using Beutl.Engine;
using Beutl.Validation;

namespace Beutl.AgentToolkit.Reconciliation;

public enum ValidationStatus
{
    Ok,
    Coerced,
    Rejected,
}

public sealed record ValidationOutcome(
    ValidationStatus Status,
    object? OriginalValue,
    object? CoercedValue,
    string? Message)
{
    public static ValidationOutcome Ok(object? value)
    {
        return new ValidationOutcome(ValidationStatus.Ok, value, value, null);
    }

    public static ValidationOutcome Coerced(object? original, object? coerced)
    {
        return new ValidationOutcome(ValidationStatus.Coerced, original, coerced, null);
    }

    public static ValidationOutcome Rejected(object? original, string message)
    {
        return new ValidationOutcome(ValidationStatus.Rejected, original, original, message);
    }
}

public static class ValidationEvaluator
{
    public static ValidationOutcome Evaluate(ICoreObject target, CoreProperty property, object? value)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(property);

        if (!IsAssignableValue(property.PropertyType, value))
        {
            return ValidationOutcome.Rejected(value, $"Value is not assignable to {property.PropertyType.FullName}.");
        }

        IValidator? validator = property.GetMetadata<ICorePropertyMetadata>(target.GetType()).GetValidator();
        return EvaluateValidator(validator, new ValidationContext(target, property), value);
    }

    public static ValidationOutcome Evaluate(IProperty property, object? value)
    {
        ArgumentNullException.ThrowIfNull(property);

        if (!IsAssignableValue(property.ValueType, value))
        {
            return ValidationOutcome.Rejected(value, $"Value is not assignable to {property.ValueType.FullName}.");
        }

        IValidator validator = property.CreateValidator(property.GetAttributes() ?? []);
        return EvaluateValidator(validator, new ValidationContext(property, null), value);
    }

    private static ValidationOutcome EvaluateValidator(IValidator? validator, ValidationContext context, object? value)
    {
        if (validator is null)
        {
            return ValidationOutcome.Ok(value);
        }

        object? coerced = value;
        if (validator.TryCoerce(context, ref coerced))
        {
            return Equals(value, coerced)
                ? ValidationOutcome.Ok(value)
                : ValidationOutcome.Coerced(value, coerced);
        }

        string? message = validator.Validate(context, value);
        return message is null
            ? ValidationOutcome.Ok(value)
            : ValidationOutcome.Rejected(value, message);
    }

    private static bool IsAssignableValue(Type targetType, object? value)
    {
        if (value is null)
        {
            return !targetType.IsValueType || Nullable.GetUnderlyingType(targetType) is not null;
        }

        return targetType.IsInstanceOfType(value);
    }
}
