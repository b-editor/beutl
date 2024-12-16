using System.ComponentModel.DataAnnotations;
using Beutl.Graphics.Rendering;
using Beutl.Graphics3D.Transformation;
using Beutl.Language;

namespace Beutl.Graphics3D;

public class Drawable3D : Renderable
{
    public static readonly CoreProperty<ITransform3D?> TransformProperty;

    private ITransform3D? _transform;

    static Drawable3D()
    {
        TransformProperty = ConfigureProperty<ITransform3D?, Drawable3D>(nameof(Transform))
            .Accessor(o => o.Transform, (o, v) => o.Transform = v)
            .DefaultValue(null)
            .Register();

        AffectsRender<Drawable3D>(TransformProperty);
        Hierarchy<Drawable3D>(TransformProperty);
    }

    [Display(Name = nameof(Strings.Transform), ResourceType = typeof(Strings), GroupName = nameof(Strings.Transform))]
    public ITransform3D? Transform
    {
        get => _transform;
        set => SetAndRaise(TransformProperty, ref _transform, value);
    }

    public virtual void Render(GraphicsContext3D context)
    {
    }
}
