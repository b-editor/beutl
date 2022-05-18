using System.Text.Json.Nodes;

using BeUtl.Collections;

namespace BeUtl.Configuration;

public sealed class ExtensionConfig : ConfigurationBase
{
    public struct TypeLazy
    {
        private Type? _type = null;

        public TypeLazy(string formattedTypeName)
        {
            FormattedTypeName = formattedTypeName;
        }

        public string FormattedTypeName { get; init; }

        public Type? Type => _type ??= TypeFormat.ToType(FormattedTypeName);
    }

    // Keyには拡張子を含める
    public CoreDictionary<string, ICoreList<TypeLazy>> EditorExtensions { get; } = new();

    public override void FromJson(JsonNode json)
    {
        base.FromJson(json);
        if (json is JsonObject jsonObject)
        {
            if (jsonObject.TryGetPropertyValue("editor-extensions", out JsonNode? eeNode)
                && eeNode is JsonObject eeObject)
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
        }
    }

    public override JsonNode ToJson()
    {
        JsonNode jsonNode = base.ToJson();

        var eeObject = new JsonObject();
        foreach ((string key, ICoreList<TypeLazy> value) in EditorExtensions)
        {
            eeObject.Add(key, new JsonArray(value
                .Select(type => type.FormattedTypeName)
                .Select(str => JsonValue.Create(str))
                .ToArray()));
        }

        jsonNode["editor-extensions"] = eeObject;
        return JsonNode;
    }
}
