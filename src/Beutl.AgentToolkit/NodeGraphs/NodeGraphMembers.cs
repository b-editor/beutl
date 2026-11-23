using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Documents;
using Beutl.AgentToolkit.Reconciliation;
using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Extensibility;
using Beutl.JsonConverters;
using Beutl.NodeGraph;
using Beutl.Serialization;

namespace Beutl.AgentToolkit.NodeGraphs;

// How an agent names, reads and writes the members of a node.
internal static class NodeGraphMembers
{
    // The text monitor a node reports its failures in, by the convention every node that can fail follows.
    public const string ErrorMonitorName = "Error";

    public static string KindOf(INodeMember member) => member switch
    {
        INodeMonitor => "monitor",
        IOutputPort => "output",
        IListInputPort => "listInput",
        IInputPort => "input",
        _ => "property",
    };

    // A root member goes by its own name; a nested input by its root's name and the path to its property.
    public static string NameOf(GraphNode node, INodeMember member)
    {
        if (member is not INestedInputPort nested)
        {
            return member.Name.Value;
        }

        string root = node.Items.FirstOrDefault(item => item.Id.Value == nested.RootMember.Id)?.Name.Value ?? "?";
        IEnumerable<string> path = nested.PropertyPath.Select(segment => segment.StartsWith("p:", StringComparison.Ordinal)
            ? segment[2..]
            : $"[{(segment.StartsWith("i:", StringComparison.Ordinal) ? segment[2..] : segment)}]");
        return $"{root}.{string.Join('.', path)}";
    }

    // A member by name. A group shows an inner input and output that share a name, so where several match, the one
    // that fits wins: a settable member for a value, an output or an input for that end of a connection.
    public static INodeMember? Find(GraphNode node, string name, Func<INodeMember, bool>? prefer = null)
    {
        INodeMember? first = null;
        foreach (INodeMember item in node.EnumerateMembers())
        {
            if (NameOf(node, item) != name)
            {
                continue;
            }

            if (prefer is null || prefer(item))
            {
                return item;
            }

            first ??= item;
        }

        return first;
    }

    public static Type? ValueTypeOf(INodeMember member)
    {
        if (member.AssociatedType is { } associated)
        {
            return associated;
        }

        if (member.Property?.PropertyType is { } propertyType)
        {
            return propertyType;
        }

        // A monitor shows a value of its own type, which it does not report as an associated type.
        for (Type? type = member.GetType(); type is not null; type = type.BaseType)
        {
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(NodeMonitor<>))
            {
                return type.GetGenericArguments()[0];
            }
        }

        return null;
    }

    // Whether the member holds a value of its own: an input or a property, or an output that carries an authored value,
    // such as a graph input of LayerInputNode. An output that carries what the node makes has no adapter.
    public static bool IsSettable(INodeMember member)
        => member is not INodeMonitor && member.Property is { IsReadOnly: false };

    public static bool IsCode(INodeMember member)
        => member.Property?.GetAttributes().OfType<DataTypeAttribute>().Any(attribute => attribute.IsCode()) == true;

    // The failure a node reports, or null.
    public static string? ReadError(GraphNode node)
    {
        foreach (INodeMember item in node.Items)
        {
            if (item is NodeMonitor<string?> monitor
                && monitor.Name.Value == ErrorMonitorName
                && !string.IsNullOrWhiteSpace(monitor.Value))
            {
                return monitor.Value;
            }
        }

        return null;
    }

    // What the member holds as an agent writes it: a value, keyframes as $kf, or a monitor's text. Null when it holds
    // nothing a document could carry, such as a wire.
    public static JsonNode? ReadValue(INodeMember member)
    {
        if (member is NodeMonitor<string?> text)
        {
            return text.Value is { } message ? JsonValue.Create(message) : null;
        }

        if (member is INodeMonitor || member.Property is not { } adapter)
        {
            return null;
        }

        if (adapter is IAnimatablePropertyAdapter { Animation: { } animation })
        {
            return WriteKeyFrames(animation);
        }

        return adapter.GetValue() is { } value && IsData(value.GetType()) ? Serialize(value) : null;
    }

    // What a new value of the type looks like, to show in a message about a value that did not convert.
    public static string? Example(INodeMember member)
    {
        if (member.Property is not { } adapter || !IsData(adapter.PropertyType))
        {
            return null;
        }

        object? value = adapter.GetValue();
        if (value is null && adapter.PropertyType.IsValueType)
        {
            value = Activator.CreateInstance(adapter.PropertyType);
        }

        return value is null ? null : Serialize(value)?.ToJsonString();
    }

    public static void WriteValue(INodeMember member, string name, JsonNode? json, Rational? frameRate)
    {
        if (member is INodeMonitor || member.Property is null && member is IOutputPort)
        {
            throw Rejected(
                $"'{name}' is {(member is INodeMonitor ? "a monitor" : "an output")}; it shows what the node makes and takes no value.",
                "Set inputs and properties only. read_node_graph lists each member's kind.");
        }

        if (member.Property is not { IsReadOnly: false } adapter)
        {
            throw Rejected($"'{name}' takes no value.", "Connect it instead, or set another member.");
        }

        if (json is JsonObject obj && KeyFrameShorthand.IsShorthand(obj))
        {
            if (adapter is not IAnimatablePropertyAdapter animatable)
            {
                throw Rejected($"'{name}' cannot be animated.", "Give it a plain value instead of $kf.");
            }

            animatable.Animation = ReadKeyFrames(obj, adapter.PropertyType, name, frameRate);
            return;
        }

        object? value = Convert(json, adapter.PropertyType, name, Example(member));
        // A plain value is what the member holds from now on, so it replaces keyframes.
        if (adapter is IAnimatablePropertyAdapter { Animation: not null } animated)
        {
            animated.Animation = null;
        }

        adapter.SetValue(value);
    }

    // A readable C# form of the type, such as float, Vector4 or ShaderGraphValue<ShaderImage>.
    public static string FormatType(Type? type)
    {
        if (type is null)
        {
            return "none";
        }

        if (Nullable.GetUnderlyingType(type) is { } underlying)
        {
            return FormatType(underlying) + "?";
        }

        if (type.IsArray)
        {
            return FormatType(type.GetElementType()) + "[]";
        }

        if (type == typeof(float)) return "float";
        if (type == typeof(double)) return "double";
        if (type == typeof(int)) return "int";
        if (type == typeof(uint)) return "uint";
        if (type == typeof(long)) return "long";
        if (type == typeof(ulong)) return "ulong";
        if (type == typeof(bool)) return "bool";
        if (type == typeof(byte)) return "byte";
        if (type == typeof(string)) return "string";
        if (type == typeof(object)) return "object";

        if (type.IsGenericType)
        {
            string name = type.Name;
            int tick = name.IndexOf('`', StringComparison.Ordinal);
            if (tick >= 0)
            {
                name = name[..tick];
            }

            return $"{name}<{string.Join(", ", type.GetGenericArguments().Select(FormatType))}>";
        }

        return type.Name;
    }

    // Values a document carries as data: numbers, text, enums, structures and byte arrays, not objects or wires.
    private static bool IsData(Type type)
        => type.IsValueType || type == typeof(string) || type == typeof(byte[]);

    private static JsonNode? Serialize(object value)
    {
        // Files hold an enum as its number; an agent reads and writes its name, as apply_edit takes it.
        if (value is Enum)
        {
            return JsonValue.Create(value.ToString());
        }

        try
        {
            return CoreSerializer.SerializeToJsonNode(value);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static object? Convert(JsonNode? json, Type type, string name, string? example)
    {
        string expected = example is null
            ? $"'{name}' takes {FormatType(type)}."
            : $"'{name}' takes {FormatType(type)}, written like {example}.";
        if (json is null)
        {
            if (type.IsValueType && Nullable.GetUnderlyingType(type) is null)
            {
                throw Rejected($"{expected} null is not one.", "Give a value of that type.");
            }

            return null;
        }

        object? value;
        try
        {
            value = EnumJsonValueNormalizer.Deserialize(json, type);
        }
        catch (Exception ex)
        {
            IReadOnlyList<string>? names = EnumJsonValueNormalizer.GetEnumNames(type);
            throw Rejected(
                $"{expected} {json.ToJsonString()} is not one: {ex.Message}",
                names is null ? "Give a value of that type." : $"Give one of {string.Join(", ", names)}.");
        }

        if (value is null ? type.IsValueType && Nullable.GetUnderlyingType(type) is null : !type.IsInstanceOfType(value))
        {
            throw Rejected($"{expected} {json.ToJsonString()} is not one.", "Give a value of that type.");
        }

        return value;
    }

    private static IKeyFrameAnimation ReadKeyFrames(JsonObject json, Type valueType, string name, Rational? frameRate)
    {
        // The shorthand names an easing; one with settings of its own, such as a spline's control points, comes as the
        // object a keyframe saves, and goes into the keyframe it belongs to after expanding.
        var shorthand = (JsonObject)json.DeepClone();
        var easingObjects = new Dictionary<int, JsonObject>();
        if (shorthand[KeyFrameShorthand.PropertyName] is JsonArray entries)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i] is JsonArray { Count: 3 } tuple && tuple[2] is JsonObject tupleEasing)
                {
                    easingObjects[i] = (JsonObject)tupleEasing.DeepClone();
                    tuple.RemoveAt(2);
                }
                else if (entries[i] is JsonObject entry && (entry["easing"] ?? entry["Easing"]) is JsonObject entryEasing)
                {
                    easingObjects[i] = (JsonObject)entryEasing.DeepClone();
                    entry.Remove("easing");
                    entry.Remove("Easing");
                }
            }
        }

        JsonObject expanded;
        try
        {
            expanded = KeyFrameShorthand.Expand(shorthand, valueType);
        }
        catch (ReconcileException ex)
        {
            throw Rejected($"'{name}': {ex.Error.Message}", ex.Error.Hint);
        }

        if (expanded[nameof(IKeyFrameAnimation.KeyFrames)] is JsonArray keyFrames)
        {
            for (int i = 0; i < keyFrames.Count; i++)
            {
                if (keyFrames[i] is not JsonObject keyFrame)
                {
                    continue;
                }

                // An enum takes its name here too, as a plain value does.
                if (keyFrame["Value"] is { } value)
                {
                    keyFrame["Value"] = EnumJsonValueNormalizer.Normalize(value, valueType);
                }

                if (easingObjects.TryGetValue(i, out JsonObject? easing))
                {
                    keyFrame[nameof(IKeyFrame.Easing)] = easing;
                }
            }
        }

        try
        {
            // Times read on the project's frame grid, as apply_edit reads them.
            using IDisposable? grid = frameRate is { } rate ? MediaTimeJsonConverter.ReadOnFrameGrid(rate) : null;
            var animation = (IKeyFrameAnimation)CoreSerializer.DeserializeFromJsonObject(
                expanded, typeof(KeyFrameAnimation<>).MakeGenericType(valueType));
            animation.KeyFrames.SortByKeyTime();
            return animation;
        }
        catch (Exception ex)
        {
            throw Rejected(
                $"'{name}' takes keyframes of {FormatType(valueType)}; these did not read: {ex.Message}",
                "Give each keyframe as [seconds, value, easing?] with a value of the member's type.");
        }
    }

    // In the form edit_node_graph takes, so that writing it back keeps the motion.
    private static JsonObject WriteKeyFrames(IKeyFrameAnimation animation)
    {
        var entries = new JsonArray();
        foreach (IKeyFrame keyFrame in animation.KeyFrames)
        {
            var entry = new JsonArray
            {
                keyFrame.KeyTime.Value.ToTimeSpan().TotalSeconds,
                keyFrame.Value is { } value ? Serialize(value) : null
            };
            if (keyFrame.Easing.Value is { } easing and not LinearEasing)
            {
                entry.Add(WriteEasing(easing));
            }

            entries.Add(entry);
        }

        var json = new JsonObject { [KeyFrameShorthand.PropertyName] = entries };
        if (animation.UseGlobalClock.Value)
        {
            json[nameof(IKeyFrameAnimation.UseGlobalClock)] = true;
        }

        return json;
    }

    // An easing by its name, or, when it has settings of its own such as a spline's control points, as a keyframe saves it.
    private static JsonNode WriteEasing(Easing easing)
    {
        JsonNode? saved = Serialize(easing);
        return saved is JsonObject settings && settings.Any(pair => pair.Key != "$type")
            ? settings
            : JsonValue.Create(easing.GetType().Name);
    }

    private static ReconcileException Rejected(string message, string? hint)
        => new(new ToolError(ErrorCode.ValidationRejected, message, null, hint));
}
