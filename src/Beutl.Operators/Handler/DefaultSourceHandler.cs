using Beutl.Operation;
using Beutl.Rendering;

namespace Beutl.Operators.Handler;

public sealed class DefaultSourceHandler : SourceOperator
{
    public override void Evaluate(OperatorEvaluationContext context)
    {
        for (int i = 0; i < context.FlowRenderables.Count; i++)
        {
            Renderable item = context.FlowRenderables[i];

            context.FlowRenderables.RemoveAt(i);
            context.AddRenderable(item);
            i--;
        }
    }
}
