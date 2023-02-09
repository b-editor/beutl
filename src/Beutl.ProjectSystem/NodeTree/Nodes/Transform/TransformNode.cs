using Beutl.Graphics;
using Beutl.Graphics.Transformation;

namespace Beutl.NodeTree.Nodes.Transform;

public class TransformNode : Node
{
    private readonly InputSocket<Drawable> _inputSocket;
    private readonly OutputSocket<Drawable> _outputSocket;
    private readonly InputSocket<Matrix> _matrixSocket;
    private readonly MatrixTransform _model = new();
    private Drawable? _prevDrawable;

    public TransformNode()
    {
        _inputSocket = AsInput<Drawable>("Input");
        _outputSocket = AsOutput<Drawable>("Output");
        _matrixSocket = AsInput<Matrix>("Matrix", "Matrix");
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

        if (_matrixSocket.Connection != null)
        {
            _model.Matrix = _matrixSocket.Value;
        }
        else
        {
            _model.Matrix = Matrix.Identity;
        }

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
