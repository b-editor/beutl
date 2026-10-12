using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using Beutl.Audio;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Shapes;
using Beutl.Serialization;

namespace Beutl.ProjectSystem;

internal static class ElementMigration
{
    // The operators that Beutl 1.x releases wrote. Operators that only unreleased builds wrote are not migrated.
    private static readonly FrozenDictionary<string, Type> s_typeMap = FrozenDictionary.Create(
        new KeyValuePair<string, Type>("[Beutl.Operators].Source:EllipseOperator", typeof(EllipseShape)),
        new KeyValuePair<string, Type>("[Beutl.Operators].Source:RectOperator", typeof(RectShape)),
        new KeyValuePair<string, Type>("[Beutl.Operators].Source:RoundedRectOperator", typeof(RoundedRectShape)),
        new KeyValuePair<string, Type>("[Beutl.Operators].Source:GeometryOperator", typeof(GeometryShape)),
        new KeyValuePair<string, Type>("[Beutl.Operators].Source:TextBlockOperator", typeof(TextBlock)),
        new KeyValuePair<string, Type>("[Beutl.Operators].Source:SourceVideoOperator", typeof(SourceVideo)),
        new KeyValuePair<string, Type>("[Beutl.Operators].Source:SourceImageOperator", typeof(SourceImage)),
        new KeyValuePair<string, Type>("[Beutl.Operators].Source:SourceBackdropOperator", typeof(SourceBackdrop)),
        new KeyValuePair<string, Type>("[Beutl.Operators].Source:SourceSoundOperator", typeof(SourceSound)),
        new KeyValuePair<string, Type>("[Beutl.ProjectSystem]Beutl.Operation:DecorateOperator", typeof(DrawableDecorator)),
        new KeyValuePair<string, Type>("[Beutl.ProjectSystem]Beutl.Operation:GroupOperator", typeof(DrawableGroup)));

    // The type Beutl 1.x wrote for an element's node tree. Nothing in 2.x provides it.
    internal const string LegacyNodeTreeTypeName = "[Beutl.ProjectSystem]Beutl.NodeTree:ElementNodeTreeModel";

    // Beutl 1.x drew an element whose UseNode was on through its node tree instead of its operators.
    // 2.x cannot read those nodes, so the tree becomes content that could not be loaded: recovery then
    // keeps the element's original file, and removing this object is the explicit way to drop the tree.
    internal static bool TryCreateUnmigratedNodeTree(
        IJsonSerializationContext jsonContext,
        [NotNullWhen(true)] out FallbackEngineObject? nodeTree)
    {
        nodeTree = null;
        if (jsonContext.GetNode("UseNode") is not JsonValue useNode
            || !useNode.TryGetValue(out bool used)
            || !used
            || jsonContext.GetNode("NodeTree") is not JsonObject tree
            || tree["Nodes"] is not JsonArray { Count: > 0 })
        {
            return false;
        }

        JsonObject json = tree.DeepClone().AsObject();
        json["$type"] = LegacyNodeTreeTypeName;
        nodeTree = new FallbackEngineObject { Json = json, Reason = FallbackReason.TypeNotFound };
        return true;
    }

    internal static bool IsUnmigratedNodeTree(EngineObject obj)
    {
        return obj is FallbackEngineObject fallback
               && fallback.TryGetTypeName(out string? typeName)
               && typeName == LegacyNodeTreeTypeName;
    }

    internal static bool TryMigrateFromOperation(
        IJsonSerializationContext jsonContext,
        out EngineObject[] migrated)
    {
        if (jsonContext.GetNode("Operation") is not JsonObject operation)
        {
            migrated = [];
            return false;
        }

        if (operation["Children"] is not JsonArray children)
        {
            migrated = [];
            return false;
        }

        var results = new List<EngineObject>();
        foreach (JsonNode? child in children)
        {
            if (child is not JsonObject operatorObj)
                continue;

            EngineObject engineObject = ExtractEngineObjectFromOperator(operatorObj, jsonContext);
            results.Add(engineObject);
        }

        migrated = results.ToArray();
        return true;
    }

    private static EngineObject ExtractEngineObjectFromOperator(JsonObject operatorObj,
        IJsonSerializationContext jsonContext)
    {
        string? operatorType = null;
        // TakeAfterOperator の移行
        if (operatorObj.TryGetPropertyValue("$type", out JsonNode? typeNode)
            && typeNode is JsonValue typeValue
            && typeValue.TryGetValue(out operatorType)
            && operatorType.Contains("TakeAfterOperator"))
        {
            var portal = new PortalObject();
            if (operatorObj["Count"] is JsonValue countValue
                && countValue.TryGetValue(out int count))
            {
                portal.Count.CurrentValue = count;
            }

            return portal;
        }

        // PublishOperator<T> 系: "Value" プロパティにEngineObjectを保持
        if (operatorObj["Value"] is JsonObject valueObj)
        {
            try
            {
                if (operatorType != null
                    && s_typeMap.TryGetValue(operatorType, out Type? targetType))
                {
                    return (EngineObject)CoreSerializer.DeserializeFromJsonObject(
                        valueObj, targetType,
                        new CoreSerializerOptions { BaseUri = jsonContext.BaseUri });
                }

                throw new InvalidOperationException("Unknown type");
            }
            catch (Exception ex)
            {
                // デシリアライズ失敗時はFallbackEngineObjectにJSON保存
                var dummy = new FallbackEngineObject();
                dummy.Json = valueObj.DeepClone().AsObject();
                dummy.Reason = FallbackReason.DeserializationFailed;
                dummy.ErrorMessage = ex.Message;
                return dummy;
            }
        }

        // 不明なOperator: FallbackEngineObjectにJSON保存
        var fallback = new FallbackEngineObject();
        fallback.Json = operatorObj.DeepClone().AsObject();
        return fallback;
    }
}
