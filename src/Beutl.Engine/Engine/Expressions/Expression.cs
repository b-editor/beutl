using System.Diagnostics.CodeAnalysis;
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
            throw new NotSupportedException("Unsupported expression type.");
        }
    }
}
