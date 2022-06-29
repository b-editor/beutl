using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

using BeUtl.Animation;

namespace BeUtl.Styling;

public abstract class Styleable : Element, IStyleable
{
    public static readonly CoreProperty<Styles> StylesProperty;
    private readonly Styles _styles;
    private IStyleInstance? _styleInstance;

    static Styleable()
    {
        StylesProperty = ConfigureProperty<Styles, Styleable>(nameof(Styles))
            .Accessor(o => o.Styles, (o, v) => o.Styles = v)
            .Register();
    }

    protected Styleable()
    {
        _styles = new()
        {
            Attached = item => item.Invalidated += Style_Invalidated,
            Detached = item => item.Invalidated -= Style_Invalidated
        };
        _styles.CollectionChanged += Style_Invalidated;
    }

    private void Style_Invalidated(object? sender, EventArgs e)
    {
        InvalidateStyles();
    }

    public Styles Styles
    {
        get => _styles;
        set
        {
            if (_styles != value)
            {
                _styles.Replace(value);
            }
        }
    }

    public void InvalidateStyles()
    {
        if (_styleInstance != null)
        {
            _styleInstance.Dispose();
            _styleInstance = null;
        }
    }

    public void ApplyStyling(IClock clock)
    {
        if (_styleInstance == null)
        {
            _styleInstance = Styles.Instance(this);
        }

        if (_styleInstance != null)
        {
            _styleInstance.Begin();
            _styleInstance.Apply(clock);
            _styleInstance.End();
        }
    }

    IStyleInstance? IStyleable.GetStyleInstance(IStyle style)
    {
        IStyleInstance? styleInstance = _styleInstance;
        while (styleInstance != null)
        {
            if (styleInstance.Source == style)
            {
                return styleInstance;
            }
        }

        return null;
    }

    void IStyleable.StyleApplied(IStyleInstance instance)
    {
        _styleInstance = instance;
    }

    private static Style? ReadStyleFromJsonObject(JsonObject json)
    {
        if (json.TryGetPropertyValue("target", out JsonNode? targetNode)
            && targetNode is JsonValue targetValue
            && targetValue.TryGetValue(out string? targetStr)
            && TypeFormat.ToType(targetStr) is Type targetType)
        {
            var style = new Style
            {
                TargetType = targetType
            };

            if (json.TryGetPropertyValue("setters", out JsonNode? settersNode)
                && settersNode is JsonArray settersArray)
            {
                foreach (JsonNode? item in settersArray)
                {
                    if (item != null && ReadSetterFromJson(item, targetType) is ISetter setter)
                    {
                        style.Setters.Add(setter);
                    }
                }
            }

            return style;
        }

        return null;
    }

    private static ISetter? ReadSetterFromJson(JsonNode json, Type targetType)
    {
        if (json is JsonObject jobj)
        {
            if (jobj.TryGetPropertyValue("property", out JsonNode? propertyNode)
                && propertyNode is JsonValue propertyValue
                && propertyValue.TryGetValue(out string? propertyStr))
            {
                int separator = propertyStr.IndexOf(":");
                CoreProperty? property = null;
                if (separator < 0)
                {
                    property = PropertyRegistry.FindRegistered(targetType, propertyStr);
                }
                else
                {
                    string name = propertyStr[..separator];
                    var type = TypeFormat.ToType(propertyStr[separator..]);
                    if (type != null)
                    {
                        property = PropertyRegistry.FindRegistered(type, name);
                    }
                }

                if (property == null)
                    return null;

                var helper = (IGenericHelper)typeof(GenericHelper<>)
                    .MakeGenericType(property.PropertyType)
                    .GetField("Instance")!
                    .GetValue(null)!;

                object? value = null;
                if (jobj.TryGetPropertyValue("value", out JsonNode? valueNode))
                {
                    value = DeserializeValue(property.PropertyType, valueNode!);
                }

                var animations = new List<BaseAnimation>();

                if (jobj.TryGetPropertyValue("animations", out JsonNode? animationsNode)
                    && animationsNode is JsonArray animationsArray)
                {
                    if (animations.Capacity < animationsArray.Count)
                    {
                        animations.Capacity = animationsArray.Count;
                    }
                    foreach (JsonNode? item in animationsArray)
                    {
                        if (item is JsonObject animationObj)
                        {
                            animations.Add(helper.DeserializeAnimation(animationObj));
                        }
                    }
                }

                return helper.InitializeSetter(property, value, animations);
            }
        }

        return null;
    }

    private static object? DeserializeValue(Type type, JsonNode jsonNode)
    {
        if (jsonNode is JsonObject jsonObject
            && jsonObject.TryGetPropertyValue("@type", out JsonNode? atTypeNode)
            && atTypeNode is JsonValue atTypeValue
            && atTypeValue.TryGetValue(out string? atTypeStr)
            && TypeFormat.ToType(atTypeStr) is Type realType
            && realType.IsAssignableTo(type)
            && realType.IsAssignableTo(typeof(IJsonSerializable)))
        {
            var sobj = (IJsonSerializable?)Activator.CreateInstance(realType);
            if (sobj != null)
            {
                sobj.ReadFromJson(jsonNode!);
                return sobj;
            }
        }
        else if (type.IsAssignableTo(typeof(IJsonSerializable)))
        {
            var sobj = (IJsonSerializable?)Activator.CreateInstance(type);
            if (sobj != null)
            {
                sobj.ReadFromJson(jsonNode!);
                return sobj;
            }
        }
        else
        {
            return JsonSerializer.Deserialize(jsonNode, type, JsonHelper.SerializerOptions);
        }

        return type.IsValueType ? Activator.CreateInstance(type) : null;
    }

    private static JsonNode? SerializeValue(Type type, object value)
    {
        if (value is IJsonSerializable child)
        {
            JsonNode jsonNode = new JsonObject();
            child.WriteToJson(ref jsonNode);

            var objType = value.GetType();
            if (objType != type && jsonNode is JsonObject)
            {
                jsonNode["@type"] = TypeFormat.ToString(objType);
            }

            return jsonNode;
        }
        else
        {
            return JsonSerializer.SerializeToNode(value, type, JsonHelper.SerializerOptions);
        }
    }

    public override void ReadFromJson(JsonNode json)
    {
        base.ReadFromJson(json);
        if (json is JsonObject jobject)
        {
            if (jobject.TryGetPropertyValue("styles", out JsonNode? stylesNode)
                && stylesNode is JsonArray stylesArray)
            {
                Styles.Clear();
                if (Styles.Capacity < stylesArray.Count)
                {
                    Styles.Capacity = stylesArray.Count;
                }

                foreach (JsonNode? styleNode in stylesArray)
                {
                    if (styleNode is JsonObject styleObject
                        && ReadStyleFromJsonObject(styleObject) is Style style)
                    {
                        Styles.Add(style);
                    }
                }
            }
        }
    }

    private static JsonNode WriteSetterToJson(ISetter setter, Type targetType)
    {
        var json = new JsonObject();

        if (targetType.IsAssignableTo(setter.Property.OwnerType))
        {
            json["property"] = setter.Property.Name;
        }
        else
        {
            json["property"] = $"{setter.Property.Name}:{TypeFormat.ToString(setter.Property.OwnerType)}";
        }

        if (setter.Value != null)
        {
            json["value"] = SerializeValue(setter.Property.PropertyType, setter.Value);
        }

        if (setter.Animations.Any())
        {
            var animations = new JsonArray();

            foreach (var item in setter.Animations)
            {
                JsonNode anmNode = new JsonObject();
                item.WriteToJson(ref anmNode);
                animations.Add(anmNode);
            }

            json["animations"] = animations;
        }

        return json;
    }

    public override void WriteToJson(ref JsonNode json)
    {
        base.WriteToJson(ref json);
        if (json is JsonObject jobject)
        {
            if (Styles.Count > 0)
            {
                var styles = new JsonArray();

                foreach (IStyle style in Styles.AsSpan())
                {
                    var styleJson = new JsonObject
                    {
                        ["target"] = TypeFormat.ToString(style.TargetType)
                    };

                    var setters = new JsonArray();
                    foreach (ISetter? item in style.Setters)
                    {
                        setters.Add(WriteSetterToJson(item, style.TargetType));
                    }
                    styleJson["setters"] = setters;

                    styles.Add(styleJson);
                }

                jobject["styles"] = styles;
            }
        }
    }

    private interface IGenericHelper
    {
        ISetter InitializeSetter(CoreProperty property, object? value, IEnumerable<BaseAnimation> animations);

        BaseAnimation DeserializeAnimation(JsonObject json);
    }

    private sealed class GenericHelper<T> : IGenericHelper
    {
        public static readonly GenericHelper<T> Instance = new();

        public ISetter InitializeSetter(CoreProperty property, object? value, IEnumerable<BaseAnimation> animations)
        {
            var setter = new Setter<T>((CoreProperty<T>)property, (T?)value);
            setter.Animations.AddRange(animations.OfType<Animation<T>>());
            return setter;
        }

        public BaseAnimation DeserializeAnimation(JsonObject json)
        {
            var anm = new Animation<T>();
            anm.ReadFromJson(json);
            return anm;
        }
    }
}
