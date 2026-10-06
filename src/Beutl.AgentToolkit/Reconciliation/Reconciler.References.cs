using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Common;
using Beutl.Engine;

namespace Beutl.AgentToolkit.Reconciliation;

public sealed partial class Reconciler
{
    // A scene reference is set through the Expressions form (ReferenceExpression), which resolves its
    // ObjectId against the project at composition time. A direct value on the property cannot resolve
    // that way — the engine would mint a new empty object from it — so any non-null direct value is
    // rejected here, and the Expressions form (if present) is validated for a resolvable target.
    private static void ValidateReferenceValues(CoreObject liveRoot, JsonNode? node, string path = "$")
    {
        if (node is JsonObject obj)
        {
            foreach (ReferencePropertyDescriptor property in ResolveReferenceProperties(liveRoot, obj))
            {
                ValidateReferenceExpression(liveRoot, obj, property, path);
                if (obj.TryGetPropertyValue(property.Name, out JsonNode? valueNode) && valueNode is not null)
                {
                    throw new ReconcileException(new ToolError(
                        ErrorCode.ValidationRejected,
                        InlineReferenceMessage(property.Name, property.ReferencedType.Name),
                        $"{path}/{property.Name}",
                        InlineReferenceHint(property.Name, property.ReferencedType.Name)));
                }
            }

            foreach (KeyValuePair<string, JsonNode?> pair in obj.ToArray())
            {
                ValidateReferenceValues(liveRoot, pair.Value, $"{path}/{pair.Key}");
            }
        }
        else if (node is JsonArray array)
        {
            for (int i = 0; i < array.Count; i++)
            {
                ValidateReferenceValues(liveRoot, array[i], $"{path}[{i}]");
            }
        }
    }

    // The Expressions reference form carries its target in ObjectId and resolves only at composition
    // time, so an unknown, all-zero, or wrong-typed Id would render the referencing object silently
    // empty; it is validated against the live project here instead. A scene reference names its
    // target directly by Id — the PropertyPath form (resolving the target from a property value on
    // another object) is not a form the toolkit emits and cannot be attached to a project-detached
    // render snapshot, so it is rejected rather than left to compose empty at render time.
    private static void ValidateReferenceExpression(
        CoreObject liveRoot, JsonObject obj, ReferencePropertyDescriptor property, string path)
    {
        if (!obj.TryGetPropertyValue("Expressions", out JsonNode? expressionsNode)
            || expressionsNode is not JsonObject expressions
            || !expressions.TryGetPropertyValue(property.Name, out JsonNode? expressionNode)
            || expressionNode is null)
        {
            return;
        }

        string typeName = property.ReferencedType.Name;
        string expressionPath = $"{path}/Expressions/{property.Name}";
        // A present expression node must be a reference object carrying an ObjectId. Any other shape
        // (a missing or misspelled ObjectId, or a bare value) parses to no expression at all, which
        // would silently clear the reference instead of setting it.
        if (expressionNode is not JsonObject expressionObject
            || !expressionObject.TryGetPropertyValue("ObjectId", out JsonNode? objectIdNode))
        {
            throw new ReconcileException(new ToolError(
                ErrorCode.ValidationRejected,
                $"{property.Name}: the reference expression must be an object with a Guid ObjectId.",
                expressionPath,
                $"Set Expressions.{property.Name} = {{\"ObjectId\": \"<guid>\"}} referencing an existing {typeName}."));
        }

        // Match Expression.CreateFromNode: it only builds a ReferenceExpression when ObjectId is a
        // scalar Guid value. An object form ({ "Id": ... }) would pass a lenient check here but
        // deserialize to no expression at all — a silently dropped reference.
        if (objectIdNode is not JsonValue objectIdValue || !objectIdValue.TryGetValue(out Guid objectId))
        {
            throw new ReconcileException(new ToolError(
                ErrorCode.ValidationRejected,
                $"{property.Name}: the expression's ObjectId must be a Guid string.",
                expressionPath,
                $"Set the expression's ObjectId to the Guid Id of an existing {typeName}, e.g. \"ObjectId\": \"<guid>\"."));
        }

        if (objectId == Guid.Empty)
        {
            throw new ReconcileException(new ToolError(
                ErrorCode.ValidationRejected,
                $"{property.Name}: the all-zero Guid is not a valid {typeName} reference.",
                expressionPath,
                $"Pass the Id of an existing {typeName}, or remove the expression."));
        }

        // HasPropertyPath is IsNullOrEmpty, so an empty path is a direct-object reference and passes.
        if (expressionObject.TryGetPropertyValue("PropertyPath", out JsonNode? propertyPathNode)
            && propertyPathNode is JsonValue propertyPathValue
            && propertyPathValue.TryGetValue(out string? propertyPathText)
            && !string.IsNullOrEmpty(propertyPathText))
        {
            throw new ReconcileException(new ToolError(
                ErrorCode.ValidationRejected,
                $"{property.Name}: a PropertyPath on the reference expression is not supported; reference the {typeName} directly by its Id.",
                expressionPath,
                $"Set Expressions.{property.Name} = {{\"ObjectId\": \"<guid>\"}} without a PropertyPath."));
        }

        ICoreObject searchRoot = (liveRoot as IHierarchical)?.FindHierarchicalRoot() as ICoreObject ?? liveRoot;
        ICoreObject? target = searchRoot.FindById(objectId);
        if (target is null || !property.ReferencedType.IsInstanceOfType(target))
        {
            throw new ReconcileException(new ToolError(
                ErrorCode.ValidationRejected,
                $"{property.Name}: no {typeName} with Id '{objectId}' exists in the project.",
                expressionPath,
                $"Pass the Id of an existing {typeName}, or create it first."));
        }
    }

    private static string InlineReferenceMessage(string propertyName, string typeName)
    {
        return $"{propertyName} references an existing {typeName} and is set through the Expressions form, not as a direct value.";
    }

    private static string InlineReferenceHint(string propertyName, string typeName)
    {
        return $"Read the project document for the target {typeName} Id, then set Expressions.{propertyName} = {{\"ObjectId\": \"<guid>\"}}.";
    }

    private static IReadOnlyList<ReferencePropertyDescriptor> ResolveReferenceProperties(CoreObject liveRoot, JsonObject obj)
    {
        if (obj.TryGetDiscriminator(out Type? type) && type is not null)
        {
            return ReferenceProperties.ForOwner(type);
        }

        if (CollectionReconciler.TryGetId(obj, out Guid id)
            && IdentityHelper.FindById(liveRoot, id) is EngineObject live)
        {
            return ReferenceProperties.ForOwner(live.GetType());
        }

        return [];
    }
}
