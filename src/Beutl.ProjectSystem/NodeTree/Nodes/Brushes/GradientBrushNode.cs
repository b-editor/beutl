using Beutl.Media;

namespace Beutl.NodeTree.Nodes.Brushes;

public class GradientBrushNode<T> : BrushNode<T>
    where T : GradientBrush.Resource, new()
{
    public GradientBrushNode()
    {
        SpreadMethod = AsInput<GradientSpreadMethod>("SpreadMethod");
        GradientStops = AsInput<List<GradientStop.Resource>>("GradientStops");
        GradientStops.Value = [];
    }

    protected InputSocket<GradientSpreadMethod> SpreadMethod { get; }

    protected InputSocket<List<GradientStop.Resource>> GradientStops { get; }

    public override void Evaluate(NodeEvaluationContext context)
    {
        base.Evaluate(context);
        GradientBrush.Resource? brush = context.GetOrDefaultState<GradientBrush.Resource>();
        if (brush == null) return;

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
