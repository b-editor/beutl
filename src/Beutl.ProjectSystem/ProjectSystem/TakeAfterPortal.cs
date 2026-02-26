using System.ComponentModel.DataAnnotations;
using Beutl.Collections.Pooled;
using Beutl.Engine;
using Beutl.Language;

namespace Beutl.ProjectSystem;

[Display(Name = nameof(Strings.Portal), ResourceType = typeof(Strings))]
public sealed partial class TakeAfterPortal : EngineObject, IFlowOperator
{
    public TakeAfterPortal()
    {
        ScanProperties<TakeAfterPortal>();
    }

    public IProperty<int> Count { get; } = Property.Create<int>();

    void IFlowOperator.ProcessFlow(IList<EngineObject> flow, EvaluationTarget target, object? renderer)
    {
        if (renderer is SceneRenderer sceneRenderer)
        {
            using var portalFlow = new PooledList<EngineObject>();

            int start = ZIndex + 1;
            int end = ZIndex + Count.CurrentValue;

            List<Element> elements = sceneRenderer.CurrentElements;
            for (int i = 0; i < elements.Count; i++)
            {
                Element item = elements[i];
                if (item.ZIndex >= start && item.ZIndex <= end)
                {
                    elements.RemoveAt(i);
                    i--;

                    using (PooledList<EngineObject> result = item.Evaluate(target, sceneRenderer))
                    {
                        portalFlow.AddRange(result.Span);
                    }
                }
                else if (item.ZIndex > end)
                {
                    break;
                }
            }

            foreach (EngineObject item in portalFlow.Span)
            {
                flow.Add(item);
            }
        }
    }

    void IFlowOperator.EnterFlow()
    {
    }

    void IFlowOperator.ExitFlow()
    {
    }

    void IFlowOperator.OnSerializing()
    {
    }
}
