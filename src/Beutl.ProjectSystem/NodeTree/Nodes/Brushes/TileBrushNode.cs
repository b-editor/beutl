using Beutl.Graphics;
using Beutl.Media;

namespace Beutl.NodeTree.Nodes.Brushes;

public class TileBrushNode<T> : BrushNode<T>
    where T : TileBrush, new()
{
    public TileBrushNode()
    {
        AlignmentX = AsInput(TileBrush.AlignmentXProperty);
        AlignmentY = AsInput(TileBrush.AlignmentYProperty);
        DestinationRect = AsInput(TileBrush.DestinationRectProperty);
        SourceRect = AsInput(TileBrush.SourceRectProperty);
        Stretch = AsInput(TileBrush.StretchProperty);
        TileMode = AsInput(TileBrush.TileModeProperty);
        BitmapInterpolationMode = AsInput(TileBrush.BitmapInterpolationModeProperty);
    }

    protected InputSocket<AlignmentX> AlignmentX { get; }

    protected InputSocket<AlignmentY> AlignmentY { get; }

    protected InputSocket<RelativeRect> DestinationRect { get; }

    protected InputSocket<RelativeRect> SourceRect { get; }

    protected InputSocket<Stretch> Stretch { get; }

    protected InputSocket<TileMode> TileMode { get; }

    protected InputSocket<BitmapInterpolationMode> BitmapInterpolationMode { get; }

    public override void Evaluate(NodeEvaluationContext context)
    {
        base.Evaluate(context);
        TileBrush? brush = context.GetOrDefaultState<TileBrush>();
        if (brush != null)
        {
            brush.AlignmentX = AlignmentX.Value;
            brush.AlignmentY = AlignmentY.Value;
            brush.DestinationRect = DestinationRect.Value;
            brush.SourceRect = SourceRect.Value;
            brush.Stretch = Stretch.Value;
            brush.TileMode = TileMode.Value;
            brush.BitmapInterpolationMode = BitmapInterpolationMode.Value;
        }
    }
}
