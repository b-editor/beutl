using System.Text.Json;
using System.Text.Json.Nodes;

using BeUtl.Animation;

namespace BeUtl.Styling;

public static class StyleSerializer
{
    public static JsonNode ToJson(this IStyle style)
    {
        var styleJson = new JsonObject
        {
            ["target"] = TypeFormat.ToString(style.TargetType)
        };

        var setters = new JsonArray();
        foreach (ISetter? item in style.Setters)
        {
            setters.Add(item.ToJson(style.TargetType));
        }
        styleJson["setters"] = setters;

        return styleJson;
    }

    public static JsonNode ToJson(this ISetter setter, Type targetType)
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

            foreach (IAnimation item in setter.Animations)
            {
                JsonNode anmNode = new JsonObject();
                item.WriteToJson(ref anmNode);
                animations.Add(anmNode);
            }

            json["animations"] = animations;
        }

        return json;
    }

    public static JsonNode? SerializeValue(Type type, object value)
    {
        if (value is IJsonSerializable serializable)
        {
            JsonNode jsonNode = new JsonObject();
            serializable.WriteToJson(ref jsonNode);

            Type? objType = value.GetType();
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

    public static Style? ToStyle(this JsonObject json)
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
                    if (item != null && item.ToSetter(targetType) is ISetter setter)
                    {
                        style.Setters.Add(setter);
                    }
                }
            }

            return style;
        }

        return null;
    }

    public static ISetter? ToSetter(this JsonNode json, Type targetType)
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

    public static object? DeserializeValue(Type type, JsonNode jsonNode)
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
