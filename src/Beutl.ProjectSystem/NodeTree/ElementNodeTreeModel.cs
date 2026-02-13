using Beutl.Animation;
using Beutl.Collections.Pooled;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.ProjectSystem;

namespace Beutl.NodeTree;

public class ElementNodeTreeModel : NodeTreeModel
{
    private readonly NodeTreeEvaluator _evaluator;

    public ElementNodeTreeModel()
    {
        _evaluator = new NodeTreeEvaluator(this);
    }

    public PooledList<EngineObject> Evaluate(EvaluationTarget target, IRenderer renderer, Element element)
    {
        _ = target;
        _evaluator.Build(renderer);

        var list = new PooledList<EngineObject>();
        try
        {
            foreach (NodeEvaluationContext[]? item in _evaluator.EvalContexts)
            {
                foreach (NodeEvaluationContext? context in item)
                {
                    context.Target = target;
                    context._renderables = list;
                }
            }

            _evaluator.Evaluate();

            // Todo: LayerOutputNodeに移動
            foreach (EngineObject item in list.Span)
            {
                item.ZIndex = element.ZIndex;
                item.TimeRange = new TimeRange(element.Start, element.Length);
            }

            return list;
        }
        catch
        {
            list.Dispose();
            throw;
        }
    }
}
