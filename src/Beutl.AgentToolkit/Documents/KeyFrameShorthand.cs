using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Reconciliation;
using Beutl.Animation;
using Beutl.Animation.Easings;

namespace Beutl.AgentToolkit.Documents;

// The long form spells out what is already known: the value type comes from the property, and an
// easing name is unambiguous within Beutl.Animation.Easings.
internal static class KeyFrameShorthand
{
    public const string PropertyName = "$kf";

    private static readonly Dictionary<string, Type> s_easings = typeof(Easing).Assembly
        .GetTypes()
        .Where(type => !type.IsAbstract && typeof(Easing).IsAssignableFrom(type))
        .ToDictionary(type => type.Name, type => type, StringComparer.OrdinalIgnoreCase);

    public static bool IsShorthand(JsonObject animationJson)
    {
        ArgumentNullException.ThrowIfNull(animationJson);
        return animationJson.ContainsKey(PropertyName);
    }

    public static JsonObject Expand(JsonObject animationJson, Type valueType)
    {
        ArgumentNullException.ThrowIfNull(animationJson);
        ArgumentNullException.ThrowIfNull(valueType);

        if (animationJson[PropertyName] is not JsonArray entries)
        {
            throw Rejected($"'{PropertyName}' must be an array of keyframes.",
                "Use [[seconds, value, easing?], ...] or [{\"t\": seconds, \"v\": value, \"easing\": name}, ...].");
        }

        string keyFrameType = IdentityHelper.WriteDiscriminator(typeof(KeyFrame<>).MakeGenericType(valueType));
        var keyFrames = new JsonArray();
        for (int i = 0; i < entries.Count; i++)
        {
            (double seconds, JsonNode? value, string? easing) = ReadEntry(entries[i], i);
            var keyFrame = new JsonObject
            {
                ["$type"] = keyFrameType,
                [nameof(IKeyFrame.KeyTime)] = TimeSpan.FromSeconds(seconds).ToString("c", CultureInfo.InvariantCulture),
                ["Value"] = value?.DeepClone()
            };

            if (!string.IsNullOrWhiteSpace(easing))
            {
                keyFrame[nameof(IKeyFrame.Easing)] = IdentityHelper.WriteDiscriminator(ResolveEasing(easing));
            }

            keyFrames.Add(keyFrame);
        }

        var expanded = new JsonObject
        {
            ["$type"] = IdentityHelper.WriteDiscriminator(typeof(KeyFrameAnimation<>).MakeGenericType(valueType)),
            [nameof(KeyFrameAnimation.KeyFrames)] = keyFrames
        };

        // Anything the caller set alongside the shorthand (UseGlobalClock, Id, ...) still applies.
        foreach ((string key, JsonNode? node) in animationJson)
        {
            if (key != PropertyName && key != "$type")
            {
                expanded[key] = node?.DeepClone();
            }
        }

        return expanded;
    }

    private static (double Seconds, JsonNode? Value, string? Easing) ReadEntry(JsonNode? entry, int index)
    {
        switch (entry)
        {
            case JsonArray tuple when tuple.Count is 2 or 3:
                return (ReadSeconds(tuple[0], index), tuple[1], tuple.Count == 3 ? tuple[2]?.GetValue<string>() : null);
            case JsonObject obj:
                JsonNode? time = obj["t"] ?? obj["keyTime"] ?? obj["KeyTime"];
                JsonNode? value = obj.TryGetPropertyValue("v", out JsonNode? shortValue)
                    ? shortValue
                    : obj.TryGetPropertyValue("value", out JsonNode? longValue)
                        ? longValue
                        : obj["Value"];
                JsonNode? easing = obj["easing"] ?? obj["Easing"];
                return (ReadSeconds(time, index), value, easing?.GetValue<string>());
            default:
                throw Rejected(
                    $"Keyframe {index} in '{PropertyName}' is neither a [seconds, value, easing?] array nor an object.",
                    "Use [[0, 0, \"CubicEaseOut\"], [0.4, 100]] or [{\"t\": 0, \"v\": 0, \"easing\": \"CubicEaseOut\"}].");
        }
    }

    private static double ReadSeconds(JsonNode? node, int index)
    {
        // A JsonValue keeps the CLR type it was constructed from, so a literal 0 does not answer
        // TryGetValue<double>; every numeric backing type has to be tried explicitly.
        if (node is JsonValue value)
        {
            if (value.TryGetValue(out double asDouble)) return asDouble;
            if (value.TryGetValue(out float asFloat)) return asFloat;
            if (value.TryGetValue(out decimal asDecimal)) return (double)asDecimal;
            if (value.TryGetValue(out long asLong)) return asLong;
            if (value.TryGetValue(out int asInt)) return asInt;
            if (value.TryGetValue(out JsonElement element)
                && element.ValueKind == JsonValueKind.Number
                && element.TryGetDouble(out double asElement))
            {
                return asElement;
            }

            if (value.TryGetValue(out string? text)
                && TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out TimeSpan parsed))
            {
                return parsed.TotalSeconds;
            }
        }

        throw Rejected(
            $"Keyframe {index} in '{PropertyName}' has no readable time.",
            "Give the time as seconds (0.4) or as a TimeSpan string (\"00:00:00.4000000\").");
    }

    private static Type ResolveEasing(string name)
    {
        if (s_easings.TryGetValue(name.Trim(), out Type? type))
        {
            return type;
        }

        throw Rejected(
            $"Easing '{name}' is not a Beutl easing.",
            $"Use a bare easing type name such as {string.Join(", ", s_easings.Keys.Order(StringComparer.Ordinal).Take(6))}, ... (no assembly prefix).");
    }

    private static ReconcileException Rejected(string message, string hint)
    {
        return new ReconcileException(new ToolError(ErrorCode.ValidationRejected, message, PropertyName, hint));
    }
}
