using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Beutl.Engine.Expressions;

public static class Expression
{
    public static StringExpression<T> Create<T>(string expressionString)
    {
        return new StringExpression<T>(expressionString);
    }

    public static bool TryParse<T>(string expressionString, [NotNullWhen(true)] out StringExpression<T>? expression,
        [NotNullWhen(false)] out string? error)
    {
        expression = new StringExpression<T>(expressionString);
        if (expression.Validate(out error))
        {
            return true;
        }

        expression = null;
        return false;
    }

    public static ReferenceExpression<T> CreateReference<T>(Guid objectId)
        where T : CoreObject
    {
        return new ReferenceExpression<T>(objectId);
    }

    public static ReferenceExpression<T> CreateReference<T>(Guid objectId, string propertyPath)
    {
        return new ReferenceExpression<T>(objectId, propertyPath);
    }

    public static IExpression<T>? CreateFromNode<T>(JsonNode node)
    {
        if (node is JsonValue valueNode && valueNode.TryGetValue(out string? exprString))
        {
            return new StringExpression<T>(exprString);
        }
        else if (node is JsonObject objNode)
        {
            if (objNode.TryGetDiscriminator(out Type? type))
            {
                return JsonSerializer.Deserialize(objNode, WithTimeArgument<T>(type, typeof(TimeSpan), typeof(MediaTime)))
                    as IExpression<T>;
            }

            if (objNode.TryGetPropertyValue("ObjectId", out JsonNode? objectIdNode) &&
                objectIdNode is JsonValue objectIdValueNode &&
                objectIdValueNode.TryGetValue(out Guid objectId))
            {
                string? propertyPath = null;
                if (objNode.TryGetPropertyValue("PropertyPath", out JsonNode? propertyPathNode) &&
                    propertyPathNode is JsonValue propertyPathValueNode)
                {
                    propertyPathValueNode.TryGetValue(out propertyPath);
                }

                return new ReferenceExpression<T>(objectId, propertyPath);
            }
        }

        return null;
    }

    public static JsonNode ToNode<T>(IExpression<T> expression)
    {
        if (expression is StringExpression<T> stringExpression)
        {
            return stringExpression.ExpressionString;
        }
        else if (expression is ReferenceExpression<T> referenceExpression)
        {
            var obj = new JsonObject
            {
                ["ObjectId"] = JsonValue.Create(referenceExpression.ObjectId)
            };

            if (referenceExpression.HasPropertyPath)
            {
                obj["PropertyPath"] = JsonValue.Create(referenceExpression.PropertyPath);
            }

            return obj;
        }
        else
        {
            var node = JsonSerializer.SerializeToNode(expression, expression.GetType());
            if (node is not JsonObject obj)
            {
                throw new InvalidOperationException("Serialized expression is not a JsonObject.");
            }
            obj.WriteDiscriminator(WithTimeArgument<T>(expression.GetType(), typeof(MediaTime), typeof(TimeSpan)));
            return obj;
        }
    }

    // Until #2885 step 4 raises the format floor, files name an expression of a MediaTime property with TimeSpan.
    private static Type WithTimeArgument<T>(Type type, Type from, Type to)
    {
        if (typeof(T) != typeof(MediaTime) || !type.IsGenericType || type.GenericTypeArguments is not [var argument]
            || argument != from)
        {
            return type;
        }

        try
        {
            return type.GetGenericTypeDefinition().MakeGenericType(to);
        }
        catch (ArgumentException)
        {
            return type;
        }
    }
}
