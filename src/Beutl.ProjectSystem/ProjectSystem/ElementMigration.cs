using System.Collections.Frozen;
using System.Text.Json.Nodes;
using Beutl.Audio;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Particles;
using Beutl.Graphics.Shapes;
using Beutl.Graphics3D;
using Beutl.Graphics3D.Lighting;
using Beutl.Graphics3D.Models;
using Beutl.Graphics3D.Primitives;
using Beutl.NodeTree;
using Beutl.Serialization;

namespace Beutl.ProjectSystem;

internal static class ElementMigration
{
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
        new KeyValuePair<string, Type>("[Beutl.Operators].Source:ParticleEmitterOperator", typeof(ParticleEmitter)),
        new KeyValuePair<string, Type>("[Beutl.Operators].Source:NodeTreeOperator", typeof(NodeTreeDrawable)),
        new KeyValuePair<string, Type>("[Beutl.ProjectSystem]Beutl.Operation:DecorateOperator", typeof(DrawableDecorator)),
        new KeyValuePair<string, Type>("[Beutl.ProjectSystem]Beutl.Operation:GroupOperator", typeof(DrawableGroup)),
        new KeyValuePair<string, Type>("[Beutl.ProjectSystem]Beutl.Operation:SoundGroupOperator", typeof(SoundGroup)),
        new KeyValuePair<string, Type>("[Beutl.Operators].Source:DrawableTimeControllerOperator", typeof(DrawableTimeController)),
        new KeyValuePair<string, Type>("[Beutl.Operators].Source:Scene3DOperator", typeof(Scene3D)),
        new KeyValuePair<string, Type>("[Beutl.Operators].Source:Cube3DOperator", typeof(Cube3D)),
        new KeyValuePair<string, Type>("[Beutl.Operators].Source:Sphere3DOperator", typeof(Sphere3D)),
        new KeyValuePair<string, Type>("[Beutl.Operators].Source:Plane3DOperator", typeof(Plane3D)),
        new KeyValuePair<string, Type>("[Beutl.Operators].Source:Model3DOperator", typeof(Model3D)),
        new KeyValuePair<string, Type>("[Beutl.Operators].Source:DirectionalLight3DOperator", typeof(DirectionalLight3D)),
        new KeyValuePair<string, Type>("[Beutl.Operators].Source:PointLight3DOperator", typeof(PointLight3D)),
        new KeyValuePair<string, Type>("[Beutl.Operators].Source:SpotLight3DOperator", typeof(SpotLight3D)));

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

            EngineObject engineObject = ExtractEngineObjectFromOperator(operatorObj, jsonContext);
            results.Add(engineObject);
        }

        return results.ToArray();
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
                if (operatorType != null
                    && s_typeMap.TryGetValue(operatorType, out Type? targetType))
                {
                    return (EngineObject)CoreSerializer.DeserializeFromJsonObject(
                        valueObj, targetType,
                        new CoreSerializerOptions { BaseUri = jsonContext.BaseUri });
                }

                throw new InvalidOperationException("Unknown type");
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
