using Beutl.Graphics.Transformation;

namespace Beutl.NodeTree.Nodes.Transform;

public sealed class TransformNodeEvaluationState
{
    public TransformNodeEvaluationState(TransformGroup? created, object? addtionalState)
    {
        Created = created;
        AddtionalState = addtionalState;
    }

    public TransformGroup? Created { get; set; }

    public object? AddtionalState { get; set; }
}

public abstract class TransformNode : Node
{
    public TransformNode()
    {
        OutputSocket = AsOutput<TransformGroup>("TransformGroup");
        InputSocket = AsInput<TransformGroup?>("TransformGroup");
    }

    protected OutputSocket<TransformGroup> OutputSocket { get; }

    protected InputSocket<TransformGroup?> InputSocket { get; }

    public override void Evaluate(NodeEvaluationContext context)
    {
        TransformGroup? value = InputSocket.Value;
        var state = context.State as TransformNodeEvaluationState;
        if (state == null)
        {
            context.State = state = new TransformNodeEvaluationState(null, null);
        }

        if (value == null)
        {
            state.Created ??= new TransformGroup();
            value = state.Created;
        }

        EvaluateCore(value, state.AddtionalState);
        OutputSocket.Value = value;
    }

    protected abstract void EvaluateCore(TransformGroup group, object? state);
}
