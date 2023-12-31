using Beutl.Media;

namespace Beutl.NodeTree.Nodes.Brushes;

public class GradientBrushNode<T> : BrushNode<T>
    where T : GradientBrush, new()
{
    public GradientBrushNode()
    {
        SpreadMethod = AsInput(GradientBrush.SpreadMethodProperty);
        GradientStops = AsInput(GradientBrush.GradientStopsProperty, []);
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
