using System.Text.Json.Nodes;

using Beutl.Collections;
using Beutl.Serialization;

namespace Beutl.Configuration;

public sealed class ExtensionConfig : ConfigurationBase
{
    public ExtensionConfig()
    {
        EditorExtensions.CollectionChanged += (_, _) => OnChanged();
        DecoderPriority.CollectionChanged += (_, _) => OnChanged();
    }

    public struct TypeLazy(string formattedTypeName)
    {
        private Type? _type = null;

        public string FormattedTypeName { get; init; } = formattedTypeName;

        public Type? Type => _type ??= TypeFormat.ToType(FormattedTypeName);
    }

    // Keyには拡張子を含める
    public CoreDictionary<string, ICoreList<TypeLazy>> EditorExtensions { get; } = [];

    // Keyには拡張子を含める
    public CoreList<TypeLazy> DecoderPriority { get; } = [];

    [ObsoleteSerializationApi]
    public override void ReadFromJson(JsonObject json)
    {
        base.ReadFromJson(json);
        JsonNode? GetNode(string name1, string name2)
        {
            if (json[name1] is JsonNode node1)
                return node1;
            else if (json[name2] is JsonNode node2)
                return node2;
            else
                return null;
        }

        if (GetNode("editor-extensions", nameof(EditorExtensions)) is JsonObject eeObject)
        {
            EditorExtensions.Clear();
            foreach (KeyValuePair<string, JsonNode?> item in eeObject)
            {
                if (item.Value is JsonArray jsonArray)
                {
                    EditorExtensions.Add(item.Key, new CoreList<TypeLazy>(jsonArray.OfType<JsonValue>()
                        .Select(value => value.TryGetValue(out string? type) ? type : null)
                        .Select(str => new TypeLazy(str!))
                        .Where(type => type.FormattedTypeName != null)!));
                }
            }
        }

        if (GetNode("decoder-priority", nameof(DecoderPriority)) is JsonArray dpArray)
        {
            DecoderPriority.Clear();
            DecoderPriority.AddRange(dpArray
                .Select(v => v?.AsValue()?.GetValue<string?>())
                .Where(v => v != null)
                .Select(v => new TypeLazy(v!)));
        }
    }

    [ObsoleteSerializationApi]
    public override void WriteToJson(JsonObject json)
    {
        base.WriteToJson(json);

        var eeObject = new JsonObject();
        foreach ((string key, ICoreList<TypeLazy> value) in EditorExtensions)
        {
            eeObject.Add(key, new JsonArray(value
                .Select(type => type.FormattedTypeName)
                .Select(str => JsonValue.Create(str))
                .ToArray()));
        }

        var dpArray = new JsonArray(DecoderPriority.Select(v => JsonValue.Create(v.FormattedTypeName)).ToArray());

        json[nameof(EditorExtensions)] = eeObject;
        json[nameof(DecoderPriority)] = dpArray;
    }

    public override void Serialize(ICoreSerializationContext context)
    {
        base.Serialize(context);

        context.SetValue(nameof(EditorExtensions), EditorExtensions
            .ToDictionary(x => x.Key, y => y.Value.Select(z => z.FormattedTypeName).ToArray()));

        context.SetValue(nameof(DecoderPriority), DecoderPriority.Select(v => v.FormattedTypeName).ToArray());
    }

    public override void Deserialize(ICoreSerializationContext context)
    {
        base.Deserialize(context);
        Dictionary<string, string[]>? ee = context.GetValue<Dictionary<string, string[]>>(nameof(EditorExtensions));
        EditorExtensions.Clear();
        if (ee != null)
        {
            foreach (KeyValuePair<string, string[]> item in ee)
            {
                EditorExtensions.Add(item.Key, new CoreList<TypeLazy>(item.Value
                    .Select(str => new TypeLazy(str))
                    .Where(type => type.FormattedTypeName != null)));
            }
        }

        string[]? dp = context.GetValue<string[]>(nameof(DecoderPriority));
        DecoderPriority.Clear();
        if (dp != null)
        {
            DecoderPriority.AddRange(dp
                .Where(v => v != null)
                .Select(v => new TypeLazy(v!)));
        }
    }
}
