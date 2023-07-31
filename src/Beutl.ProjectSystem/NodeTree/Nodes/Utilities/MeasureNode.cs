using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Beutl.Graphics;

namespace Beutl.NodeTree.Nodes.Utilities;

public class MeasureNode : Node
{
    private readonly OutputSocket<float> _xSocket;
    private readonly OutputSocket<float> _ySocket;
    private readonly OutputSocket<float> _widthSocket;
    private readonly OutputSocket<float> _heightSocket;
    private readonly InputSocket<Drawable> _inputSocket;

    public MeasureNode()
    {
        _xSocket = AsOutput<float>("X");
        _ySocket = AsOutput<float>("Y");
        _widthSocket = AsOutput<float>("Width");
        _heightSocket = AsOutput<float>("Height");
        _inputSocket = AsInput<Drawable>("Drawable");
    }

    public override void Evaluate(NodeEvaluationContext context)
    {
        base.Evaluate(context);
        if (_inputSocket.Value is Drawable drawable)
        {
            drawable.Measure(context.Renderer.FrameSize.ToSize(1));
            _xSocket.Value = drawable.Bounds.X;
            _ySocket.Value = drawable.Bounds.Y;
            _widthSocket.Value = drawable.Bounds.Width;
            _heightSocket.Value = drawable.Bounds.Height;
        }
    }
}
