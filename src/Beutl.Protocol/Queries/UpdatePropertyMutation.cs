using System.Text.Json;

namespace Beutl.Protocol.Queries;

public class UpdatePropertyMutation : Mutation
{
    public UpdatePropertyMutation(Guid targetId, string propertyName, object? newValue)
    {
        TargetId = targetId;
        PropertyName = propertyName;
        NewValue = newValue;
    }

    public Guid TargetId { get; }
    public string PropertyName { get; }
    public object? NewValue { get; }

    public override MutationResult Execute(MutationContext context)
    {
        ICoreObject? target = context.Root.FindById(TargetId);
        if (target == null)
        {
            return MutationResult.CreateError($"Object with ID {TargetId} not found.");
        }

        CoreProperty? property = PropertyRegistry.FindRegistered(target, PropertyName);
        if (property == null)
        {
            return MutationResult.CreateError($"Property '{PropertyName}' not found on {target.GetType().Name}.");
        }

        try
        {
            object? convertedValue = ConvertValue(NewValue, property.PropertyType);
            target.SetValue(property, convertedValue);

            return MutationResult.CreateSuccess(
                new { targetId = TargetId, propertyName = PropertyName, newValue = convertedValue },
                new Dictionary<string, object?>
                {
                    ["mutationType"] = "UpdateProperty",
                    ["executedAt"] = DateTime.UtcNow
                });
        }
        catch (Exception ex)
        {
            return MutationResult.CreateError($"Failed to update property: {ex.Message}");
        }
    }

    internal static object? ConvertValue(object? value, Type targetType)
    {
        if (value == null) return null;
        if (targetType.IsInstanceOfType(value)) return value;

        // Handle JSON element conversion
        if (value is JsonElement jsonElement)
        {
            return JsonSerializer.Deserialize(jsonElement.GetRawText(), targetType);
        }

        // Try direct conversion
        try
        {
            return Convert.ChangeType(value, targetType);
        }
        catch
        {
            // Try JSON round-trip
            var json = JsonSerializer.Serialize(value);
            return JsonSerializer.Deserialize(json, targetType);
        }
    }
}
