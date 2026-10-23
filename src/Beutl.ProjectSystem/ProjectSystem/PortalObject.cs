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

                int start = obj.ZIndex.Value + 1;
                int end = obj.ZIndex.Value + Count;

                IList<Element> elements = ctx.CurrentElements;

                for (int i = 0; i < elements.Count; i++)
                {
                    Element item = elements[i];
                    if (item.ZIndex.Value >= start && item.ZIndex.Value <= end)
                    {
                        elements.RemoveAt(i);
                        i--;
                        // Evaluate another element and get flow-processed results
                        ctx.EvaluateElementIntoFlow(item);
                    }
                    else if (item.ZIndex.Value > end) break;
                }
            }
        }
    }
}
