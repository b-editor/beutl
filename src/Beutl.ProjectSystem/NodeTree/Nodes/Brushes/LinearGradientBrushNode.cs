using Beutl.Graphics;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.Media.Immutable;

namespace Beutl.NodeTree.Nodes.Brushes;

public class BrushNode<T> : Node
    where T : Brush, new()
{
    public BrushNode()
    {
        OutputSocket = AsOutput<T>("Output", "Brush");
        OpacitySocket = AsInput(Brush.OpacityProperty).AcceptNumber();
        TransformSocket = AsInput(Brush.TransformProperty);
        TransformSocket.RegisterReceiver((object? obj, out ITransform? value) =>
        {
            if (obj is Matrix mat)
            {
                value = new ImmutableTransform(mat);
                return true;
            }
            else
            {
                value = null;
                return false;
            }
        });

        TransformOriginSocket = AsInput(Brush.TransformOriginProperty);
    }

    protected OutputSocket<T> OutputSocket { get; }

    protected InputSocket<float> OpacitySocket { get; }

    protected InputSocket<ITransform?> TransformSocket { get; }

    protected InputSocket<RelativePoint> TransformOriginSocket { get; }

    public override void Evaluate(NodeEvaluationContext context)
    {
        base.Evaluate(context);
        Brush? brush = context.GetOrDefaultState<Brush>();
        if (brush != null)
        {
            brush.Opacity = OpacitySocket.Value;
            brush.Transform = TransformSocket.Value;
            brush.TransformOrigin = TransformOriginSocket.Value;
        }
    }
}

public class GradientBrushNode<T> : BrushNode<T>
    where T : GradientBrush, new()
{
    public GradientBrushNode()
    {
        SpreadMethod = AsInput(GradientBrush.SpreadMethodProperty);
        GradientStops = AsInput(GradientBrush.GradientStopsProperty, new GradientStops());
    }

    protected InputSocket<GradientSpreadMethod> SpreadMethod { get; }

    protected InputSocket<GradientStops> GradientStops { get; }

    public override void Evaluate(NodeEvaluationContext context)
    {
        base.Evaluate(context);
        GradientBrush? brush = context.GetOrDefaultState<GradientBrush>();
        if (brush != null)
        {
            brush.SpreadMethod = SpreadMethod.Value;
            if (GradientStops.Value != null)
            {
                brush.GradientStops = GradientStops.Value;
            }
            else
            {
                brush.GradientStops.Clear();
            }
        }
    }
}

public class LinearGradientBrushNode : GradientBrushNode<LinearGradientBrush>
{
    private readonly InputSocket<RelativePoint> _startPointSocket;
    private readonly InputSocket<RelativePoint> _endPointSocket;

    public LinearGradientBrushNode()
    {
        _startPointSocket = AsInput(LinearGradientBrush.StartPointProperty);
        _endPointSocket = AsInput(LinearGradientBrush.EndPointProperty);
    }

    public override void InitializeForContext(NodeEvaluationContext context)
    {
        base.InitializeForContext(context);
        context.State = new LinearGradientBrush();
    }

    public override void UninitializeForContext(NodeEvaluationContext context)
    {
        base.UninitializeForContext(context);
        context.State = null;
    }

    public override void Evaluate(NodeEvaluationContext context)
    {
        LinearGradientBrush brush = context.GetOrSetState<LinearGradientBrush>();
        base.Evaluate(context);

        brush.StartPoint = _startPointSocket.Value;
        brush.EndPoint = _endPointSocket.Value;
        OutputSocket.Value = brush;
    }
}
