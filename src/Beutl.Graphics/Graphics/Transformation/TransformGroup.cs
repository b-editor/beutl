using System.Text.Json.Nodes;

using Beutl.Animation;

namespace Beutl.Graphics.Transformation;

public sealed class TransformGroup : Transform
{
    public static readonly CoreProperty<Transforms> ChildrenProperty;
    private readonly Transforms _children;

    static TransformGroup()
    {
        ChildrenProperty = ConfigureProperty<Transforms, TransformGroup>(nameof(Children))
            .Accessor(o => o.Children, (o, v) => o.Children = v)
            .Register();
    }

    public TransformGroup()
    {
        _children = new Transforms();
        _children.Invalidated += (_, e) => RaiseInvalidated(e);
    }

    [NotAutoSerialized]
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

    public override void ReadFromJson(JsonObject json)
    {
        base.ReadFromJson(json);
        if (json.TryGetPropertyValue(nameof(Children), out JsonNode? childrenNode)
            && childrenNode is JsonArray childrenArray)
        {
            _children.Clear();
            _children.EnsureCapacity(childrenArray.Count);

            foreach (JsonObject childJson in childrenArray.OfType<JsonObject>())
            {
                if (childJson.TryGetDiscriminator(out Type? type)
                    && type.IsAssignableTo(typeof(Transform))
                    && Activator.CreateInstance(type) is Transform transform)
                {
                    transform.ReadFromJson(childJson);
                    _children.Add(transform);
                }
            }
        }
    }

    public override void WriteToJson(JsonObject json)
    {
        base.WriteToJson(json);
        var array = new JsonArray();

        foreach (ITransform item in _children.GetMarshal().Value)
        {
            if (item is Transform transform)
            {
                var itemJson = new JsonObject();
                transform.WriteToJson(itemJson);

                itemJson.WriteDiscriminator(item.GetType());

                array.Add(itemJson);
            }
        }

        json[nameof(Children)] = array;
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
