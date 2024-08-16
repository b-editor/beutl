using Beutl.Graphics.Rendering;

namespace Beutl.Operation;

public sealed class OperatorEvaluationContext : EvaluationContext
{
    public OperatorEvaluationContext(SourceOperator sourceOperator, EvaluationContext context)
        : base(context)
    {
        Operator = sourceOperator;
    }

    public OperatorEvaluationContext(SourceOperator sourceOperator)
    {
        Operator = sourceOperator;
    }

    public IList<Renderable> FlowRenderables { get; internal set; } = null!;

    public SourceOperator Operator { get; }

    public void AddFlowRenderable(Renderable renderable)
    {
        FlowRenderables?.Add(renderable);
    }
}
