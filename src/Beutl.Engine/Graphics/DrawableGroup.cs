using System.ComponentModel.DataAnnotations;
using Beutl.Graphics.Rendering;
using Beutl.Serialization;

namespace Beutl.Graphics;

[Display(Name = "Group")]
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

    public override void Serialize(ICoreSerializationContext context)
    {
        base.Serialize(context);
        context.SetValue(nameof(Children), Children);
    }

    public override void Deserialize(ICoreSerializationContext context)
    {
        base.Deserialize(context);
        if (context.GetValue<Drawables>(nameof(Children)) is { } children)
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

        Bounds = rect.IsInvalid ? Rect.Invalid : rect.TransformToAABB(transform);
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

    public override void Render(GraphicsContext2D context)
    {
        if (IsVisible)
        {
            Size availableSize = context.Size.ToSize(1);
            Rect rect = PrivateMeasureCore(availableSize);
            if (FilterEffect != null && !rect.IsInvalid)
            {
                rect = FilterEffect.TransformBounds(rect);
            }

            Matrix transform = GetTransformMatrix(availableSize);
            Rect transformedBounds = rect.IsInvalid ? Rect.Invalid : rect.TransformToAABB(transform);

            using (context.PushBlendMode(BlendMode))
            using (context.PushLayer(transformedBounds.IsInvalid ? default : transformedBounds))
            using (context.PushTransform(transform))
            using (FilterEffect == null ? new() : context.PushFilterEffect(FilterEffect))
            using (OpacityMask == null ? new() : context.PushOpacityMask(OpacityMask, new Rect(rect.Size)))
            using (context.PushLayer())
            {
                OnDraw(context);
            }

            Bounds = transformedBounds;
        }
    }

    protected override void OnDraw(GraphicsContext2D context)
    {
        foreach (Drawable item in _children.GetMarshal().Value)
        {
            context.DrawDrawable(item);
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
