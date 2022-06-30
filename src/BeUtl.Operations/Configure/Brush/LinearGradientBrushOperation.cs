
using BeUtl.Graphics;
using BeUtl.Graphics.Transformation;
using BeUtl.Media;
using BeUtl.ProjectSystem;

namespace BeUtl.Operations.Configure.Brush;

public class LinearGradientBrushOperation : BrushOperation<LinearGradientBrush>
{
    public static readonly CoreProperty<GradientSpreadMethod> SpreadMethodProperty;
    public static readonly CoreProperty<GradientStops> GradientStopsProperty;
    public static readonly CoreProperty<RelativePoint> StartPointProperty;
    public static readonly CoreProperty<RelativePoint> EndPointProperty;

    static LinearGradientBrushOperation()
    {
        SpreadMethodProperty = ConfigureProperty<GradientSpreadMethod, LinearGradientBrushOperation>(nameof(SpreadMethod))
            .Accessor(o => o.SpreadMethod, (o, v) => o.SpreadMethod = v)
            .OverrideMetadata(new OperationPropertyMetadata<GradientSpreadMethod>
            {
                SerializeName = "spreadMethod",
                PropertyFlags = PropertyFlags.Designable
            })
            .Register();

        GradientStopsProperty = ConfigureProperty<GradientStops, LinearGradientBrushOperation>(nameof(GradientStops))
            .Accessor(o => o.GradientStops, (o, v) => o.GradientStops = v)
            .OverrideMetadata(new OperationPropertyMetadata<GradientStops>
            {
                SerializeName = "gradientStops",
                PropertyFlags = PropertyFlags.Designable
            })
            .Register();

        StartPointProperty = ConfigureProperty<RelativePoint, LinearGradientBrushOperation>(nameof(StartPoint))
            .Accessor(o => o.StartPoint, (o, v) => o.StartPoint = v)
            .OverrideMetadata(new OperationPropertyMetadata<RelativePoint>
            {
                SerializeName = "startPoint",
                PropertyFlags = PropertyFlags.Designable
            })
            .Register();

        EndPointProperty = ConfigureProperty<RelativePoint, LinearGradientBrushOperation>(nameof(EndPoint))
            .Accessor(o => o.EndPoint, (o, v) => o.EndPoint = v)
            .OverrideMetadata(new OperationPropertyMetadata<RelativePoint>
            {
                SerializeName = "endPoint",
                PropertyFlags = PropertyFlags.Designable
            })
            .Register();
    }

    public LinearGradientBrushOperation()
    {
        var pi = FindPropertyInstance(GradientStopsProperty);
        if (pi is PropertyInstance<GradientStops> pi2)
        {
            pi2.Value = Brush.GradientStops;
        }
    }

    public override float Opacity
    {
        get => Brush.Opacity * 100;
        set => Brush.Opacity = value / 100;
    }

    public override ITransform? Transform
    {
        get => Brush.Transform;
        set => Brush.Transform = value;
    }

    public override RelativePoint TransformOrigin
    {
        get => Brush.TransformOrigin;
        set => Brush.TransformOrigin = value;
    }

    public GradientSpreadMethod SpreadMethod
    {
        get => Brush.SpreadMethod;
        set => Brush.SpreadMethod = value;
    }

    public GradientStops GradientStops
    {
        get => Brush.GradientStops;
        set => Brush.GradientStops = value;
    }

    public RelativePoint StartPoint
    {
        get => Brush.StartPoint;
        set => Brush.StartPoint = value;
    }

    public RelativePoint EndPoint
    {
        get => Brush.EndPoint;
        set => Brush.EndPoint = value;
    }

    public override LinearGradientBrush Brush { get; } = new();
}
