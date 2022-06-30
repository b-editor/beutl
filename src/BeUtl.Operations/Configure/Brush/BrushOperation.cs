using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BeUtl.Graphics;
using BeUtl.Graphics.Transformation;
using BeUtl.Media;
using BeUtl.ProjectSystem;

namespace BeUtl.Operations.Configure.Brush;

public abstract class BrushOperation : LayerOperation
{
    public static readonly CoreProperty<TargetBrushProperty> TargetProperty;
    public static readonly CoreProperty<float> OpacityProperty;
    public static readonly CoreProperty<ITransform?> TransformProperty;
    public static readonly CoreProperty<RelativePoint> TransformOriginProperty;

    static BrushOperation()
    {
        TargetProperty = ConfigureProperty<TargetBrushProperty, BrushOperation>(nameof(Target))
            .Accessor(o => o.Target, (o, v) => o.Target = v)
            .OverrideMetadata(new OperationPropertyMetadata<TargetBrushProperty>
            {
                SerializeName = "target",
                PropertyFlags = PropertyFlags.Designable
            })
            .Register();

        OpacityProperty = ConfigureProperty<float, BrushOperation>(nameof(Opacity))
            .Accessor(o => o.Opacity, (o, v) => o.Opacity = v)
            .OverrideMetadata(DefaultMetadatas.Opacity)
            .Register();

        TransformProperty = ConfigureProperty<ITransform?, BrushOperation>(nameof(Transform))
            .Accessor(o => o.Transform, (o, v) => o.Transform = v)
            .OverrideMetadata(new OperationPropertyMetadata<ITransform?>
            {
                SerializeName = "transform",
                PropertyFlags = PropertyFlags.Designable
            })
            .Register();

        TransformOriginProperty = ConfigureProperty<RelativePoint, BrushOperation>(nameof(TransformOrigin))
            .Accessor(o => o.TransformOrigin, (o, v) => o.TransformOrigin = v)
            .OverrideMetadata(new OperationPropertyMetadata<RelativePoint>
            {
                SerializeName = "transformOrigin",
                PropertyFlags = PropertyFlags.Designable
            })
            .Register();
    }

    public enum TargetBrushProperty
    {
        Foreground,
        OpacityMask
    }

    public TargetBrushProperty Target { get; set; }

    public abstract float Opacity { get; set; }

    public abstract ITransform? Transform { get; set; }

    public abstract RelativePoint TransformOrigin { get; set; }
}

public abstract class BrushOperation<T> : BrushOperation
    where T : IBrush
{
    public abstract T Brush { get; }

    protected override void RenderCore(ref OperationRenderArgs args)
    {
        if (args.Result is Drawable drawable)
        {
            if (IsEnabled)
            {
                SetBrush(drawable, Brush);
            }
            else if (ReferenceEquals(GetBrush(drawable), Brush))
            {
                SetBrush(drawable, null);
            }
        }
        base.RenderCore(ref args);
    }

    private IBrush? GetBrush(Drawable drawable)
    {
        return Target == TargetBrushProperty.Foreground
            ? drawable.Foreground
            : drawable.OpacityMask;
    }

    private void SetBrush(Drawable drawable, IBrush? brush)
    {
        switch (Target)
        {
            case TargetBrushProperty.Foreground:
                drawable.Foreground = brush;
                break;
            case TargetBrushProperty.OpacityMask:
                drawable.OpacityMask = brush;
                break;
            default:
                break;
        }
    }
}
