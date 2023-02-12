using Beutl.Graphics;

namespace Beutl.NodeTree.Nodes;

public class ConfigureNodeEvaluationState
{
    public ConfigureNodeEvaluationState(Drawable? previous, object? addtionalState)
    {
        Previous = previous;
        AddtionalState = addtionalState;
    }

    public Drawable? Previous { get; set; }

    public object? AddtionalState { get; set; }
}

public abstract class ConfigureNode : Node
{
    public ConfigureNode()
    {
        OutputSocket = AsOutput<Drawable>("Output", "Drawable");
        InputSocket = AsInput<Drawable>("Input", "Drawable");
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
            context.State = new ConfigureNodeEvaluationState(value, null);
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

        EvaluateCore(context);

        OutputSocket.Value = value;
    }

    protected abstract void EvaluateCore(NodeEvaluationContext context);

    protected abstract void Attach(Drawable drawable, object? state);

    protected abstract void Detach(Drawable drawable, object? state);
}
