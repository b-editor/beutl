using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Beutl.Engine;
using Beutl.Media;
using Beutl.Serialization;
using Beutl.Validation;

namespace Beutl.AgentToolkit.Reconciliation;

// Written by name so the payload matches ReconcilePlan.ValidationStatuses, which keys off
// Status.ToString(); the Web serializer defaults would otherwise emit a bare ordinal.
[JsonConverter(typeof(JsonStringEnumConverter<ValidationStatus>))]
public enum ValidationStatus
{
    Ok,
    Warning,
    Coerced,
    Rejected,
}

public sealed record ValidationOutcome(
    ValidationStatus Status,
    JsonNode? OriginalValue,
    JsonNode? CoercedValue,
    string? Message,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Hint = null)
{
    public static ValidationOutcome Ok(object? value, CoreSerializerOptions? options = null)
    {
        JsonNode? node = ValidationValueNode.From(value, options);
        return new ValidationOutcome(ValidationStatus.Ok, node, node?.DeepClone(), null);
    }

    public static ValidationOutcome Coerced(object? original, object? coerced, CoreSerializerOptions? options = null)
    {
        return new ValidationOutcome(
            ValidationStatus.Coerced,
            ValidationValueNode.From(original, options),
            ValidationValueNode.From(coerced, options),
            null);
    }

    public static ValidationOutcome Warning(
        object? value, string message, string? hint = null, CoreSerializerOptions? options = null)
    {
        JsonNode? node = ValidationValueNode.From(value, options);
        return new ValidationOutcome(ValidationStatus.Warning, node, node?.DeepClone(), message, hint);
    }

    public static ValidationOutcome Rejected(
        object? original, string message, string? hint = null, CoreSerializerOptions? options = null)
    {
        JsonNode? node = ValidationValueNode.From(original, options);
        return new ValidationOutcome(ValidationStatus.Rejected, node, node?.DeepClone(), message, hint);
    }
}

public static class ValidationEvaluator
{
    public static ValidationOutcome Evaluate(
        ICoreObject target, CoreProperty property, object? value, CoreSerializerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(property);

        if (!IsAssignableValue(property.PropertyType, value))
        {
            return ValidationOutcome.Rejected(
                value,
                $"Value is not assignable to {property.PropertyType.FullName}.",
                CreateValueHint(property.PropertyType),
                options);
        }

        IValidator? validator = property.GetMetadata<ICorePropertyMetadata>(target.GetType()).GetValidator();
        return EvaluateValidator(validator, new ValidationContext(target, property), value, options);
    }

    public static ValidationOutcome Evaluate(
        IProperty property, object? value, CoreSerializerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(property);

        if (!IsAssignableValue(property.ValueType, value))
        {
            return ValidationOutcome.Rejected(
                value,
                $"Value is not assignable to {property.ValueType.FullName}.",
                CreateValueHint(property.ValueType),
                options);
        }

        IValidator validator = property.CreateValidator(property.GetAttributes() ?? []);
        return EvaluateValidator(validator, new ValidationContext(property, null), value, options);
    }

    private static ValidationOutcome EvaluateValidator(
        IValidator? validator, ValidationContext context, object? value, CoreSerializerOptions? options)
    {
        if (validator is null)
        {
            return ValidationOutcome.Ok(value, options);
        }

        object? coerced = value;
        if (validator.TryCoerce(context, ref coerced))
        {
            return Equals(value, coerced)
                ? ValidationOutcome.Ok(value, options)
                : ValidationOutcome.Coerced(value, coerced, options);
        }

        string? message = validator.Validate(context, value);
        return message is null
            ? ValidationOutcome.Ok(value, options)
            : ValidationOutcome.Rejected(value, message, null, options);
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
