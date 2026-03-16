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
    public IProperty<int> Count { get; } = Property.Create<int>();

    [Display(Name = nameof(Strings.Portal_Clear), ResourceType = typeof(Strings))]
    public IProperty<bool> Clear { get; } = Property.Create<bool>();

    public partial class Resource
    {
        partial void PostUpdate(PortalObject obj, CompositionContext context)
        {
            if (context is ISceneCompositionContext ctx)
            {
                if (Clear)
                {
                    context.Flow?.Clear();
                }

                int start = obj.ZIndex + 1;
                int end = obj.ZIndex + Count;

                IList<Element> elements = ctx.CurrentElements;

                for (int i = 0; i < elements.Count; i++)
                {
                    Element item = elements[i];
                    if (item.ZIndex >= start && item.ZIndex <= end)
                    {
                        elements.RemoveAt(i);
                        i--;
                        // Evaluate another element and get flow-processed results
                        ctx.EvaluateElementIntoFlow(item);
                    }
                    else if (item.ZIndex > end) break;
                }
            }
        }
    }
}
