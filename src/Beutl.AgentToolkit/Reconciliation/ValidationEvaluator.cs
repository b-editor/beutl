using System.Text.Json.Serialization;
using Beutl.Engine;
using Beutl.Media;
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
    string? Message,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Hint = null)
{
    public static ValidationOutcome Ok(object? value)
    {
        return new ValidationOutcome(ValidationStatus.Ok, value, value, null);
    }

    public static ValidationOutcome Coerced(object? original, object? coerced)
    {
        return new ValidationOutcome(ValidationStatus.Coerced, original, coerced, null);
    }

    public static ValidationOutcome Rejected(object? original, string message, string? hint = null)
    {
        return new ValidationOutcome(ValidationStatus.Rejected, original, original, message, hint);
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
            return ValidationOutcome.Rejected(
                value,
                $"Value is not assignable to {property.PropertyType.FullName}.",
                CreateValueHint(property.PropertyType));
        }

        IValidator? validator = property.GetMetadata<ICorePropertyMetadata>(target.GetType()).GetValidator();
        return EvaluateValidator(validator, new ValidationContext(target, property), value);
    }

    public static ValidationOutcome Evaluate(IProperty property, object? value)
    {
        ArgumentNullException.ThrowIfNull(property);

        if (!IsAssignableValue(property.ValueType, value))
        {
            return ValidationOutcome.Rejected(
                value,
                $"Value is not assignable to {property.ValueType.FullName}.",
                CreateValueHint(property.ValueType));
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

    internal static string? CreateValueHint(Type targetType)
    {
        Type type = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (type == typeof(Color))
        {
            return "Use serialized Beutl color values such as '#ffffb34d' or the exact color shape returned by read_document/get_schema; do not use palette names such as 'Amber'.";
        }

        if (type == typeof(Pen))
        {
            return "Pen is a typed EngineObject value. Use the Pen shape returned by get_schema/read_document, including its '$type' discriminator and PascalCase properties such as Brush and Thickness, or omit Pen when no stroke is needed.";
        }

        if (typeof(EngineObject).IsAssignableFrom(type))
        {
            return "Use a concrete '$type' discriminator returned by get_schema for this EngineObject value and only the returned PascalCase property names.";
        }

        if (type.IsEnum)
        {
            return $"Use one of the schema enum values for {type.Name}: {string.Join(", ", Enum.GetNames(type))}.";
        }

        return null;
    }
}
