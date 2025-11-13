using System.Collections;
using System.Reflection;
using System.Text.Json;

namespace Beutl.Protocol.Queries;

public class QueryExecutor
{
    public QueryResult Execute(object target, QuerySchema schema)
    {
        Dictionary<string, object?> result = ExecuteFields(target, schema.Fields);

        var metadata = new Dictionary<string, object?>
        {
            ["targetType"] = TypeFormat.ToString(target.GetType()),
            ["executedAt"] = DateTime.UtcNow
        };

        if (target is ICoreObject coreObject)
        {
            metadata["objectId"] = coreObject.Id;
        }

        return new QueryResult(result, metadata);
    }

    public QueryResult? ExecuteById(ICoreObject root, Guid targetId, QuerySchema schema)
    {
        ICoreObject? target = root.FindById(targetId);

        if (target == null)
        {
            return new QueryResult(null, new Dictionary<string, object?>
            {
                ["error"] = "Object not found",
                ["targetId"] = targetId
            });
        }

        return Execute(target, schema);
    }

    private Dictionary<string, object?> ExecuteFields(object target, QueryField[] fields)
    {
        var result = new Dictionary<string, object?>();

        foreach (QueryField field in fields)
        {
            object? value = GetFieldValue(target, field.Name);

            if (value == null)
            {
                result[field.Name] = null;
            }
            else if (field.HasSubFields)
            {
                result[field.Name] = ProcessValueWithSubFields(value, field.SubFields);
            }
            else
            {
                result[field.Name] = SerializeValue(value);
            }
        }

        return result;
    }

    private object? ProcessValueWithSubFields(object value, QueryField[] subFields)
    {
        // Handle collections
        if (value is IList list)
        {
            var resultList = new List<object?>();
            foreach (object? item in list)
            {
                if (item == null)
                {
                    resultList.Add(null);
                }
                else
                {
                    resultList.Add(ExecuteFields(item, subFields));
                }
            }
            return resultList;
        }

        // Handle single objects
        if (value is ICoreObject || IsComplexType(value))
        {
            return ExecuteFields(value, subFields);
        }

        // For primitive types with sub-fields (which doesn't make sense), return the value
        return SerializeValue(value);
    }

    private object? GetFieldValue(object target, string fieldName)
    {
        Type targetType = target.GetType();

        // Try CoreProperty first for ICoreObject
        if (target is ICoreObject coreObject)
        {
            CoreProperty? coreProperty = PropertyRegistry.FindRegistered(coreObject, fieldName);
            if (coreProperty != null)
            {
                return coreObject.GetValue(coreProperty);
            }
        }

        // Try regular property
        PropertyInfo? property = targetType.GetProperty(fieldName, BindingFlags.Public | BindingFlags.Instance);
        if (property != null)
        {
            return property.GetValue(target);
        }

        // Try field
        FieldInfo? field = targetType.GetField(fieldName, BindingFlags.Public | BindingFlags.Instance);
        if (field != null)
        {
            return field.GetValue(target);
        }

        return null;
    }

    private object? SerializeValue(object? value)
    {
        if (value == null) return null;

        Type type = value.GetType();

        // Primitive types
        if (type.IsPrimitive || type == typeof(string) || type == typeof(Guid) || type == typeof(DateTime))
        {
            return value;
        }

        // ICoreObject - return ID reference
        if (value is ICoreObject coreObject)
        {
            return new Dictionary<string, object?>
            {
                ["__ref"] = coreObject.Id,
                ["__type"] = TypeFormat.ToString(value.GetType())
            };
        }

        // Collections
        if (value is IList list)
        {
            var resultList = new List<object?>();
            foreach (object? item in list)
            {
                resultList.Add(SerializeValue(item));
            }
            return resultList;
        }

        // For other complex types, try to serialize as dictionary
        if (IsComplexType(value))
        {
            return ConvertToJsonCompatible(value);
        }

        // Fallback to string representation
        return value.ToString();
    }

    private static bool IsComplexType(object value)
    {
        Type type = value.GetType();
        return !type.IsPrimitive
            && type != typeof(string)
            && type != typeof(Guid)
            && type != typeof(DateTime)
            && !type.IsEnum;
    }

    private object? ConvertToJsonCompatible(object value)
    {
        // This converts complex objects to JSON-compatible dictionary
        try
        {
            var json = JsonSerializer.Serialize(value);
            return JsonSerializer.Deserialize<Dictionary<string, object?>>(json);
        }
        catch
        {
            // If serialization fails, return string representation
            return value.ToString();
        }
    }
}
