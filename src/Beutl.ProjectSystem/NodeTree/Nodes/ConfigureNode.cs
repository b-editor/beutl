using Beutl.Graphics;

namespace Beutl.NodeTree.Nodes;

public class ConfigureNodeEvaluationState(Drawable? previous, object? addtionalState)
{
    public Drawable? Previous { get; set; } = previous;

    public object? AddtionalState { get; set; } = addtionalState;
}

public abstract class ConfigureNode : Node
{
    public ConfigureNode()
    {
        OutputSocket = AsOutput<Drawable>("Drawable");
        InputSocket = AsInput<Drawable>("Drawable");
    }

    protected OutputSocket<Drawable> OutputSocket { get; }

    protected InputSocket<Drawable> InputSocket { get; }

    public override void UninitializeForContext(NodeEvaluationContext context)
    {
        base.UninitializeForContext(context);
        if (context.State is ConfigureNodeEvaluationState { Previous: { } } state)
        {
            Detach(state.Previous, state.AddtionalState);
            context.State = null;
        }
    }

    public override void Evaluate(NodeEvaluationContext context)
    {
        Drawable? value = InputSocket.Value;
        var state = context.State as ConfigureNodeEvaluationState;
        Drawable? prevDrawable = state?.Previous;
        if (state != null)
        {
            state.Previous = value;
        }
        else
        {
            context.State = state = new ConfigureNodeEvaluationState(value, null);
        }

        if (value != prevDrawable)
        {
            if (prevDrawable != null)
            {
                Detach(prevDrawable, state?.AddtionalState);
            }
            if (value != null)
            {
                Attach(value, state?.AddtionalState);
            }
        }

        if (value != null)
        {
            EvaluateCore(value, state?.AddtionalState);
        }

        OutputSocket.Value = value;
    }

    protected abstract void EvaluateCore(Drawable drawable, object? state);

    protected virtual void Attach(Drawable drawable, object? state)
    {
    }

    protected virtual void Detach(Drawable drawable, object? state)
    {
    }
}
