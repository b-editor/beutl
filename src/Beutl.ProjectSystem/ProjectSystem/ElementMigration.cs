using System.Text.Json.Nodes;
using Beutl.Engine;
using Beutl.Serialization;

namespace Beutl.ProjectSystem;

internal static class ElementMigration
{
    internal static EngineObject[] MigrateFromOperation(IJsonSerializationContext jsonContext)
    {
        if (jsonContext.GetNode("Operation") is not JsonObject operation)
            return [];

        if (operation["Children"] is not JsonArray children)
            return [];

        var results = new List<EngineObject>();
        foreach (JsonNode? child in children)
        {
            if (child is not JsonObject operatorObj)
                continue;

            EngineObject? engineObject = ExtractEngineObjectFromOperator(operatorObj);
            if (engineObject != null)
            {
                results.Add(engineObject);
            }
        }

        return results.ToArray();
    }

    private static EngineObject? ExtractEngineObjectFromOperator(JsonObject operatorObj)
    {
        // TakeAfterOperator の移行
        if (operatorObj.TryGetPropertyValue("$type", out JsonNode? typeNode)
            && typeNode is JsonValue typeValue
            && typeValue.TryGetValue(out string? typeStr)
            && typeStr.Contains("TakeAfterOperator"))
        {
            var portal = new TakeAfterPortal();
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
                return (EngineObject)CoreSerializer.DeserializeFromJsonObject(valueObj, typeof(EngineObject));
            }
            catch
            {
                // デシリアライズ失敗時はDummyEngineObjectにJSON保存
                var dummy = new DummyEngineObject();
                dummy.Json = valueObj.DeepClone().AsObject();
                return dummy;
            }
        }

        // 不明なOperator: DummyEngineObjectにJSON保存
        var fallback = new DummyEngineObject();
        fallback.Json = operatorObj.DeepClone().AsObject();
        return fallback;
    }
}
