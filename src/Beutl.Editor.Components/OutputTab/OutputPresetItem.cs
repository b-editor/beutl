using System.Text.Json.Nodes;
using Beutl.Serialization;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;

namespace Beutl.Editor.Components.OutputTab;

public class OutputPresetItem(OutputExtension extension, JsonObject json, string name, string? presetKey = null)
{
    public OutputExtension Extension { get; } = extension;

    public JsonObject Json { get; } = json;

    public ReactiveProperty<string> Name { get; } = new(name);

    // ロケール非依存の安定キー。組み込みのプラットフォームプリセットに付与され、
    // ユーザーの言語切替後でも重複登録を防ぐために使う。ユーザーが UI から作成した
    // プリセットは null。setter は PresetKey 未対応の保存データから読み込んだ
    // 既存アイテムにキーをバックフィルする目的でのみ使用する。
    public string? PresetKey { get; internal set; } = presetKey;

    public void Apply(ISupportOutputPreset context)
    {
        if (((IOutputContext)context).Extension.GetType() != Extension.GetType())
        {
            throw new InvalidOperationException("Extension type mismatch.");
        }

        context.Apply(Json);
    }

    public static OutputPresetItem CreateFromContext(ISupportOutputPreset context, string name)
    {
        return new OutputPresetItem(
            ((IOutputContext)context).Extension,
            context.ToPreset(),
            name);
    }

    public static JsonNode ToJson(OutputPresetItem item)
    {
        var jsonObject = new JsonObject
        {
            [nameof(Extension)] = TypeFormat.ToString(item.Extension.GetType()),
            [nameof(Json)] = item.Json.DeepClone(),
            [nameof(Name)] = item.Name.Value
        };

        if (item.PresetKey != null)
        {
            jsonObject[nameof(PresetKey)] = item.PresetKey;
        }

        return jsonObject;
    }

    public static OutputPresetItem? FromJson(JsonNode json, ILogger logger)
    {
        try
        {
            string? typeName = json[nameof(Extension)]?.ToString();
            if (typeName == null)
            {
                logger.LogError("Extension type name is null.");
                return null;
            }

            Type? type = TypeFormat.ToType(typeName);
            if (type == null)
            {
                logger.LogError("Extension type not found: {TypeName}", typeName);
                return null;
            }

            if (Activator.CreateInstance(type) is not OutputExtension extension)
            {
                logger.LogError("Failed to create instance of extension type: {TypeName}", typeName);
                return null;
            }

            if (json[nameof(Json)] is not JsonObject jsonObject)
            {
                logger.LogError("Json object is null.");
                return null;
            }

            if (json[nameof(Name)] is not JsonValue jsonValue || !jsonValue.TryGetValue(out string? name))
            {
                logger.LogError("Name is null.");
                return null;
            }

            string? presetKey = null;
            if (json[nameof(PresetKey)] is JsonValue keyValue && keyValue.TryGetValue(out string? key))
            {
                presetKey = key;
            }

            return new OutputPresetItem(extension, jsonObject, name, presetKey);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An exception has occurred while creating OutputPresetItem from JSON.");
            return null;
        }
    }
}
