using System.Collections;
using System.Reflection;

namespace Beutl.Protocol.Queries;

public class CreateObjectMutation : Mutation
{
    public CreateObjectMutation(Guid parentId, string propertyName, string typeName, Dictionary<string, object?>? initialValues = null)
    {
        ParentId = parentId;
        PropertyName = propertyName;
        TypeName = typeName;
        InitialValues = initialValues ?? new Dictionary<string, object?>();
    }

    public Guid ParentId { get; }
    public string PropertyName { get; }
    public string TypeName { get; }
    public Dictionary<string, object?> InitialValues { get; }

    public override MutationResult Execute(MutationContext context)
    {
        ICoreObject? parent = context.Root.FindById(ParentId);
        if (parent == null)
        {
            return MutationResult.CreateError($"Parent object with ID {ParentId} not found.");
        }

        CoreProperty? property = PropertyRegistry.FindRegistered(parent, PropertyName);
        if (property == null)
        {
            return MutationResult.CreateError($"Property '{PropertyName}' not found on parent.");
        }

        try
        {
            // Try to create instance of the type
            Type? type = FindType(TypeName);
            if (type == null)
            {
                return MutationResult.CreateError($"Type '{TypeName}' not found.");
            }

            if (!typeof(ICoreObject).IsAssignableFrom(type))
            {
                return MutationResult.CreateError($"Type '{TypeName}' is not an ICoreObject.");
            }

            var newObject = Activator.CreateInstance(type) as ICoreObject;
            if (newObject == null)
            {
                return MutationResult.CreateError($"Failed to create instance of '{TypeName}'.");
            }

            // Set initial values
            foreach (var kvp in InitialValues)
            {
                CoreProperty? prop = PropertyRegistry.FindRegistered(newObject, kvp.Key);
                if (prop != null)
                {
                    newObject.SetValue(prop, UpdatePropertyMutation.ConvertValue(kvp.Value, prop.PropertyType));
                }
            }

            // Set the property value
            object? currentValue = parent.GetValue(property);
            if (currentValue is IList list)
            {
                list.Add(newObject);
            }
            else
            {
                parent.SetValue(property, newObject);
            }

            return MutationResult.CreateSuccess(
                new { objectId = newObject.Id, typeName = TypeName, parentId = ParentId, propertyName = PropertyName },
                new Dictionary<string, object?>
                {
                    ["mutationType"] = "CreateObject",
                    ["executedAt"] = DateTime.UtcNow
                });
        }
        catch (Exception ex)
        {
            return MutationResult.CreateError($"Failed to create object: {ex.Message}");
        }
    }

    private static Type? FindType(string typeName)
    {
        // Search in all loaded assemblies
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type? type = assembly.GetType(typeName);
            if (type != null) return type;

            // Try with namespace prefixes
            type = assembly.GetTypes().FirstOrDefault(t => t.Name == typeName || t.FullName == typeName);
            if (type != null) return type;
        }

        return null;
    }
}
