using System.Text.Json.Nodes;

using BeUtl.Animation;
using BeUtl.Styling;

namespace BeUtl.Graphics.Transformation;

public sealed class TransformGroup : Transform
{
    public static readonly CoreProperty<Transforms> ChildrenProperty;
    private readonly Transforms _children;

    static TransformGroup()
    {
        ChildrenProperty = ConfigureProperty<Transforms, TransformGroup>(nameof(Children))
            .Accessor(o => o.Children, (o, v) => o.Children = v)
            .PropertyFlags(PropertyFlags.KnownFlags_1)
            .Register();
    }

    public TransformGroup()
    {
        _children = new Transforms();
        _children.Attached += item =>
        {
            (item as ILogicalElement)?.NotifyAttachedToLogicalTree(new(this));
            (item as IStylingElement)?.NotifyAttachedToStylingTree(new(this));
        };
        _children.Detached += item =>
        {
            (item as ILogicalElement)?.NotifyDetachedFromLogicalTree(new(this));
            (item as IStylingElement)?.NotifyDetachedFromStylingTree(new(this));
        };
        _children.Invalidated += (_, _) => RaiseInvalidated();
    }

    public Transforms Children
    {
        get => _children;
        set => _children.Replace(value);
    }

    public override Matrix Value
    {
        get
        {
            Matrix value = Matrix.Identity;

            foreach (Transform item in _children.GetMarshal().Value)
            {
                if (item.IsEnabled)
                    value = item.Value * value;
            }

            return value;
        }
    }

    public override void ReadFromJson(JsonNode json)
    {
        base.ReadFromJson(json);
        if (json is JsonObject jobject)
        {
            if (jobject.TryGetPropertyValue("children", out JsonNode? childrenNode)
                && childrenNode is JsonArray childrenArray)
            {
                _children.Clear();
                _children.EnsureCapacity(childrenArray.Count);

                foreach (JsonObject childJson in childrenArray.OfType<JsonObject>())
                {
                    if (childJson.TryGetPropertyValue("@type", out JsonNode? atTypeNode)
                        && atTypeNode is JsonValue atTypeValue
                        && atTypeValue.TryGetValue(out string? atType)
                        && TypeFormat.ToType(atType) is Type type
                        && type.IsAssignableTo(typeof(Transform))
                        && Activator.CreateInstance(type) is Transform transform)
                    {
                        transform.ReadFromJson(childJson);
                        _children.Add(transform);
                    }
                }
            }
        }
    }

    public override void WriteToJson(ref JsonNode json)
    {
        base.WriteToJson(ref json);
        if (json is JsonObject jobject)
        {
            var array = new JsonArray();

            foreach (Transform item in _children.GetMarshal().Value)
            {
                JsonNode node = new JsonObject();
                item.WriteToJson(ref node);

                node["@type"] = TypeFormat.ToString(item.GetType());

                array.Add(node);
            }

            jobject["children"] = array;
        }
    }

    public override void ApplyStyling(IClock clock)
    {
        base.ApplyStyling(clock);
        foreach (ITransform item in Children.GetMarshal().Value)
        {
            (item as Styleable)?.ApplyStyling(clock);
        }
    }

    public override void ApplyAnimations(IClock clock)
    {
        base.ApplyAnimations(clock);
        foreach (ITransform item in Children.GetMarshal().Value)
        {
            (item as Animatable)?.ApplyAnimations(clock);
        }
    }
}
