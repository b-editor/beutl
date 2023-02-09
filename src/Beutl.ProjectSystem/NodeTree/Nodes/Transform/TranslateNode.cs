using Beutl.Graphics;
using Beutl.Graphics.Transformation;

namespace Beutl.NodeTree.Nodes.Transform;

public class TranslateNode : Node
{
    private readonly InputSocket<Drawable> _inputSocket;
    private readonly OutputSocket<Drawable> _outputSocket;
    private readonly InputSocket<float> _xSocket;
    private readonly InputSocket<float> _ySocket;
    private readonly TranslateTransform _model = new();
    private Drawable? _prevDrawable;

    public TranslateNode()
    {
        _inputSocket = AsInput<Drawable>("Input");
        _outputSocket = AsOutput<Drawable>("Output");
        _xSocket = AsInput(TranslateTransform.XProperty);
        _ySocket = AsInput(TranslateTransform.YProperty);
    }

    public override void Evaluate(EvaluationContext context)
    {
        Drawable? value = _inputSocket.Value;
        if (value != _prevDrawable)
        {
            if (_prevDrawable != null)
            {
                Detach(_prevDrawable);
            }
            if (value != null)
            {
                Attach(value);
            }

            _prevDrawable = value;
        }

        _model.X = _xSocket.Value;
        _model.Y = _ySocket.Value;

        _outputSocket.Value = value;
    }

    private void Attach(Drawable drawable)
    {
        if (drawable.Transform is not TransformGroup group)
        {
            drawable.Transform = group = new TransformGroup();
        }

        group.Children.Add(_model);
    }

    private void Detach(Drawable drawable)
    {
        if (drawable.Transform is TransformGroup group)
        {
            group.Children.Remove(_model);
        }
    }

    protected override void OnDetachedFromLogicalTree(in LogicalTreeAttachmentEventArgs args)
    {
        base.OnDetachedFromLogicalTree(args);
        if (_prevDrawable != null)
        {
            Detach(_prevDrawable);
            _prevDrawable = null;
        }
    }
}
