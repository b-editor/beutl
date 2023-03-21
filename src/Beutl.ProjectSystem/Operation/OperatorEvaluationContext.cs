using Beutl.Rendering;

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

    public IList<Renderable> GlobalRenderables { get; internal set; } = null!;

    public SourceOperator Operator { get; }

    // AllowOutflowがfalseでもOutflowができる
    public void AddGlobalRenderable(Renderable renderable)
    {
        AddRenderable(renderable);
        GlobalRenderables.Add(renderable);
    }
}
