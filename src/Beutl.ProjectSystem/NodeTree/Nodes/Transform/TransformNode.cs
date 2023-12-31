using Beutl.Graphics.Transformation;

namespace Beutl.NodeTree.Nodes.Transform;

public sealed class TransformNodeEvaluationState(ITransform? created)
{
    public ITransform? Created { get; set; } = created;

    public MultiTransform? AddtionalState { get; set; }
}

public abstract class TransformNode : Node
{
    public TransformNode()
    {
        OutputSocket = AsOutput<ITransform?>("Transform");
        InputSocket = AsInput<ITransform?>("Transform");
    }

    protected OutputSocket<ITransform?> OutputSocket { get; }

    protected InputSocket<ITransform?> InputSocket { get; }

    public override void Evaluate(NodeEvaluationContext context)
    {
        ITransform? value = InputSocket.Value;
        if (context.State is not TransformNodeEvaluationState state)
        {
            context.State = state = new TransformNodeEvaluationState(null);
        }

        EvaluateCore(state.Created);
        if (value != null)
        {
            state.AddtionalState ??= new MultiTransform();
            state.AddtionalState.Left = state.Created;
            state.AddtionalState.Right = value;
            OutputSocket.Value = state.AddtionalState;
        }
        else
        {
            OutputSocket.Value = state.Created;
        }
    }

    protected abstract void EvaluateCore(ITransform? state);
}
