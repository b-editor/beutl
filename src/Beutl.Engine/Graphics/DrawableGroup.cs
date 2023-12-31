using System.Text.Json.Nodes;

using Beutl.Graphics.Effects;
using Beutl.Serialization;

namespace Beutl.Graphics;

public sealed class DrawableGroup : Drawable
{
    public static readonly CoreProperty<Drawables> ChildrenProperty;
    private readonly Drawables _children = [];

    static DrawableGroup()
    {
        ChildrenProperty = ConfigureProperty<Drawables, DrawableGroup>(nameof(Children))
            .Accessor(o => o.Children, (o, v) => o.Children = v)
            .Register();
    }

    public DrawableGroup()
    {
        _children.Invalidated += (_, e) => RaiseInvalidated(e);
        _children.Attached += HierarchicalChildren.Add;
        _children.Detached += item => HierarchicalChildren.Remove(item);
    }

    [NotAutoSerialized]
    public Drawables Children
    {
        get => _children;
        set => _children.Replace(value);
    }

    [ObsoleteSerializationApi]
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
                    && type.IsAssignableTo(typeof(Drawable))
                    && Activator.CreateInstance(type) is Drawable drawable)
                {
                    drawable.ReadFromJson(childJson);
                    _children.Add(drawable);
                }
            }
        }
    }

    [ObsoleteSerializationApi]
    public override void WriteToJson(JsonObject json)
    {
        base.WriteToJson(json);

        var array = new JsonArray();

        foreach (Drawable item in _children.GetMarshal().Value)
        {
            var itemJson = new JsonObject();
            item.WriteToJson(itemJson);
            itemJson.WriteDiscriminator(item.GetType());

            array.Add(itemJson);
        }

        json[nameof(Children)] = array;
    }

    public override void Serialize(ICoreSerializationContext context)
    {
        base.Serialize(context);
        context.SetValue(nameof(Children), Children);
    }

    public override void Deserialize(ICoreSerializationContext context)
    {
        base.Deserialize(context);
        if(context.GetValue<Drawables>(nameof(Children)) is { } children)
        {
            Children = children;
        }
    }

    public override void Measure(Size availableSize)
    {
        Rect rect = PrivateMeasureCore(availableSize);
        Matrix transform = GetTransformMatrix(availableSize);

        if (FilterEffect != null)
        {
            rect = FilterEffect.TransformBounds(rect);
        }

        Bounds = rect.TransformToAABB(transform);
    }

    private Rect PrivateMeasureCore(Size availableSize)
    {
        Rect rect = default;
        foreach (Drawable item in _children.GetMarshal().Value)
        {
            item.Measure(availableSize);
            rect = rect.Union(item.Bounds);
        }

        return rect;
    }

    public override void Render(ICanvas canvas)
    {
        if (IsVisible)
        {
            Size availableSize = canvas.Size.ToSize(1);
            Rect rect = PrivateMeasureCore(availableSize);
            if (FilterEffect != null)
            {
                rect = FilterEffect.TransformBounds(rect);
            }

            Matrix transform = GetTransformMatrix(availableSize);
            Rect transformedBounds = rect.TransformToAABB(transform);

            using (canvas.PushBlendMode(BlendMode))
            using (canvas.PushLayer(transformedBounds))
            using (canvas.PushTransform(transform))
            using (FilterEffect == null ? new() : canvas.PushFilterEffect(FilterEffect))
            using (OpacityMask == null ? new() : canvas.PushOpacityMask(OpacityMask, new Rect(rect.Size)))
            {
                OnDraw(canvas);
            }

            Bounds = transformedBounds;
        }
    }

    protected override void OnDraw(ICanvas canvas)
    {
        foreach (Drawable item in _children.GetMarshal().Value)
        {
            canvas.DrawDrawable(item);
        }
    }

    private Matrix GetTransformMatrix(Size availableSize)
    {
        Vector origin = TransformOrigin.ToPixels(availableSize);
        Matrix offset = Matrix.CreateTranslation(origin);

        if (Transform is { IsEnabled: true })
        {
            return (-offset) * Transform.Value * offset;
        }
        else
        {
            return Matrix.Identity;
        }
    }

    protected override Size MeasureCore(Size availableSize)
    {
        return PrivateMeasureCore(availableSize).Size;
    }
}
