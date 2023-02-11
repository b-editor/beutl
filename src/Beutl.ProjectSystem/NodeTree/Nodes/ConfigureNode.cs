using Beutl.Graphics;

namespace Beutl.NodeTree.Nodes;

public abstract class ConfigureNode : Node
{
    private Drawable? _prevDrawable;

    public ConfigureNode()
    {
        OutputSocket = AsOutput<Drawable>("Output", "Drawable");
        InputSocket = AsInput<Drawable>("Input", "Drawable");

        InputSocket.Disconnected += OnInputSocketDisconnected;
    }

    protected OutputSocket<Drawable> OutputSocket { get; }

    protected InputSocket<Drawable> InputSocket { get; }

    public override void Evaluate(EvaluationContext context)
    {
        Drawable? value = InputSocket.Value;
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

        EvaluateCore(context);

        OutputSocket.Value = value;
    }

    protected abstract void EvaluateCore(EvaluationContext context);

    protected abstract void Attach(Drawable drawable);

    protected abstract void Detach(Drawable drawable);

    protected override void OnDetachedFromLogicalTree(in LogicalTreeAttachmentEventArgs args)
    {
        base.OnDetachedFromLogicalTree(args);
        if (_prevDrawable != null)
        {
            Detach(_prevDrawable);
            _prevDrawable = null;
        }
    }

    private void OnInputSocketDisconnected(object? sender, SocketConnectionChangedEventArgs e)
    {
        if (_prevDrawable != null)
        {
            Detach(_prevDrawable);
            _prevDrawable = null;
        }
    }
}
