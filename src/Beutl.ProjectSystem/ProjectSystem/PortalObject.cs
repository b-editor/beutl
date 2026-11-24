using System.ComponentModel.DataAnnotations;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Language;

namespace Beutl.ProjectSystem;

[Display(Name = nameof(Strings.Portal), ResourceType = typeof(Strings))]
public sealed partial class PortalObject : EngineObject
{
    public PortalObject()
    {
        ScanProperties<PortalObject>();
    }

    [Display(Name = nameof(Strings.Portal_Count), ResourceType = typeof(Strings))]
    public IExpressionProperty<int> Count { get; } = Property.CreateExpression<int>();

    [Display(Name = nameof(Strings.Portal_Clear), ResourceType = typeof(Strings))]
    public IExpressionProperty<bool> Clear { get; } = Property.CreateExpression<bool>();

    internal (int First, int Last) GetInputLayerSpan(int count)
        => (ZIndex.Value + 1, ZIndex.Value + count);

    // A portal takes each input out of the current pass before that input can take other layers.
    internal static Element? TakeNextInput(IList<Element> elements, (int First, int Last) span, ref int index)
    {
        for (; index < elements.Count; index++)
        {
            Element item = elements[index];
            int layer = item.ZIndex.Value;
            if (layer > span.Last) break;
            if (layer < span.First) continue;

            elements.RemoveAt(index);
            return item;
        }

        return null;
    }

    public partial class Resource
    {
        partial void PostReconcile(PortalObject obj, CompositionContext context)
        {
            if (context is ISceneCompositionContext ctx)
            {
                if (Clear)
                {
                    context.Flow?.Clear();
                }

                var span = obj.GetInputLayerSpan(Count);
                IList<Element> elements = ctx.CurrentElements;
                int index = 0;
                while (TakeNextInput(elements, span, ref index) is { } item)
                {
                    ctx.EvaluateElementIntoFlow(item);
                }
            }
        }
    }
}
