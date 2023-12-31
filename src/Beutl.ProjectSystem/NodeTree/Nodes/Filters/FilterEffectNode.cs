using Beutl.Graphics.Effects;

namespace Beutl.NodeTree.Nodes.Effects;

public sealed class FilterEffectNodeEvaluationState(FilterEffect? created)
{
    public FilterEffect? Created { get; set; } = created;

    public CombinedFilterEffect? AddtionalState { get; set; }
}

public abstract class FilterEffectNode : Node
{
    public FilterEffectNode()
    {
        OutputSocket = AsOutput<FilterEffect?>("FilterEffect");
        InputSocket = AsInput<FilterEffect?>("FilterEffect");
    }

    protected OutputSocket<FilterEffect?> OutputSocket { get; }

    protected InputSocket<FilterEffect?> InputSocket { get; }

    public override void Evaluate(NodeEvaluationContext context)
    {
        FilterEffect? input = InputSocket.Value;
        if (context.State is not FilterEffectNodeEvaluationState state)
        {
            context.State = state = new FilterEffectNodeEvaluationState(null);
        }

        EvaluateCore(state.Created);
        if (input != null)
        {
            state.AddtionalState ??= new CombinedFilterEffect();
            state.AddtionalState.Second = state.Created;
            state.AddtionalState.First = input;
            OutputSocket.Value = state.AddtionalState;
        }
        else
        {
            OutputSocket.Value = state.Created;
        }
    }

    protected abstract void EvaluateCore(FilterEffect? state);
}
