using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Media;

namespace Beutl.NodeTree.Nodes;

public sealed class GeometryShapeNode : Node
{
    private readonly OutputSocket<GeometryShape> _outputSocket;
    private readonly InputSocket<Media.Geometry?> _geometrySocket;
    private readonly InputSocket<float> _widthSocket;
    private readonly InputSocket<float> _heightSocket;
    private readonly InputSocket<Stretch> _stretchSocket;
    private readonly InputSocket<PathFillType> _fillTypeSocket;
    private readonly InputSocket<IPen?> _penSocket;
    private readonly InputSocket<ITransform?> _transformSocket;

    public GeometryShapeNode()
    {
        _outputSocket = AsOutput<GeometryShape>("GeometryShape");

        _geometrySocket = AsInput<Media.Geometry?, GeometryShape>(GeometryShape.DataProperty, value: null);
        _widthSocket = AsInput<float, GeometryShape>(Shape.WidthProperty, value: -1).AcceptNumber();
        _heightSocket = AsInput<float, GeometryShape>(Shape.HeightProperty, value: -1).AcceptNumber();
        _stretchSocket = AsInput<Stretch, GeometryShape>(Shape.StretchProperty);
        _fillTypeSocket = AsInput<PathFillType, Media.Geometry>(Media.Geometry.FillTypeProperty);
        _penSocket = AsInput<IPen?, GeometryShape>(Shape.PenProperty);
        _transformSocket = AsInput<ITransform?, GeometryShape>(Drawable.TransformProperty);
    }

    public override void InitializeForContext(NodeEvaluationContext context)
    {
        base.InitializeForContext(context);
        context.State = new GeometryShape();
    }

    public override void UninitializeForContext(NodeEvaluationContext context)
    {
        base.UninitializeForContext(context);
        context.State = null;
    }

    public override void Evaluate(NodeEvaluationContext context)
    {
        GeometryShape shape = context.GetOrSetState<GeometryShape>();
        while (shape.BatchUpdate)
        {
            shape.EndBatchUpdate();
        }

        shape.BeginBatchUpdate();
        shape.Data = _geometrySocket.Value;
        if (shape.Data != null)
        {
            shape.Data.FillType = _fillTypeSocket.Value;
        }

        shape.Width = _widthSocket.Value;
        shape.Height = _heightSocket.Value;
        shape.Stretch = _stretchSocket.Value;
        shape.Pen = _penSocket.Value;
        shape.Transform = _transformSocket.Value;

        shape.BlendMode = BlendMode.SrcOver;
        shape.AlignmentX = AlignmentX.Left;
        shape.AlignmentY = AlignmentY.Top;
        shape.TransformOrigin = RelativePoint.TopLeft;

        if (shape.FilterEffect is FilterEffectGroup effectGroup)
            effectGroup.Children.Clear();
        else
            shape.FilterEffect = new FilterEffectGroup();

        _outputSocket.Value = shape;
    }
}
