using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Filters;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;

namespace Beutl.NodeTree.Nodes;

public class RectNode : Node
{
    private readonly OutputSocket<RectShape> _outputSocket;
    private readonly InputSocket<float> _widthSocket;
    private readonly InputSocket<float> _heightSocket;
    private readonly InputSocket<float> _strokeSocket;

    public RectNode()
    {
        _outputSocket = AsOutput<RectShape>("Rectangle");

        _widthSocket = AsInput<float, RectShape>(Drawable.WidthProperty, value: 100).AcceptNumber();
        _heightSocket = AsInput<float, RectShape>(Drawable.HeightProperty, value: 100).AcceptNumber();
        _strokeSocket = AsInput<float, RectShape>(RectShape.StrokeWidthProperty, value: 4000).AcceptNumber();
    }

    public override void InitializeForContext(NodeEvaluationContext context)
    {
        base.InitializeForContext(context);
        context.State = new RectShape();
    }

    public override void UninitializeForContext(NodeEvaluationContext context)
    {
        base.UninitializeForContext(context);
        context.State = null;
    }

    public override void Evaluate(NodeEvaluationContext context)
    {
        RectShape rectangle = context.GetOrSetState<RectShape>();
        while (rectangle.BatchUpdate)
        {
            rectangle.EndBatchUpdate();
        }

        rectangle.BeginBatchUpdate();
        rectangle.Width = _widthSocket.Value;
        rectangle.Height = _heightSocket.Value;
        rectangle.StrokeWidth = _strokeSocket.Value;
        rectangle.BlendMode = BlendMode.SrcOver;
        rectangle.AlignmentX = Media.AlignmentX.Left;
        rectangle.AlignmentY = Media.AlignmentY.Top;
        rectangle.TransformOrigin = RelativePoint.TopLeft;
        if (rectangle.Transform is TransformGroup transformGroup)
            transformGroup.Children.Clear();
        else
            rectangle.Transform = new TransformGroup();

        if (rectangle.Filter is ImageFilterGroup filterGroup)
            filterGroup.Children.Clear();
        else
            rectangle.Filter = new ImageFilterGroup();

        if (rectangle.Effect is BitmapEffectGroup effectGroup)
            effectGroup.Children.Clear();
        else
            rectangle.Effect = new BitmapEffectGroup();

        _outputSocket.Value = rectangle;
    }
}
