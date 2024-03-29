﻿using Beutl.Graphics;

namespace Beutl.NodeTree.Nodes;

public abstract class OutputNode : Node
{
    private Drawable? _prevDrawable;

    public OutputNode()
    {
        InputSocket = AsInput<Drawable>("Drawable");

        InputSocket.Disconnected += OnInputSocketDisconnected;
    }

    protected InputSocket<Drawable> InputSocket { get; }

    public override void Evaluate(NodeEvaluationContext context)
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
    }

    protected abstract void EvaluateCore(NodeEvaluationContext context);

    protected abstract void Attach(Drawable drawable);

    protected abstract void Detach(Drawable drawable);

    protected override void OnDetachedFromHierarchy(in HierarchyAttachmentEventArgs args)
    {
        base.OnDetachedFromHierarchy(args);
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
