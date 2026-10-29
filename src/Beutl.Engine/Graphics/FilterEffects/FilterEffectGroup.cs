using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Language;

namespace Beutl.Graphics.Effects;

[Display(Name = nameof(GraphicsStrings.Group), ResourceType = typeof(GraphicsStrings))]
public sealed partial class FilterEffectGroup : FilterEffect
{
    public FilterEffectGroup()
    {
        ScanProperties<FilterEffectGroup>();
    }

    public IListProperty<FilterEffect> Children { get; } = Property.CreateList<FilterEffect>();

    public partial class Resource
    {
        public override bool ControlsContinuation
            => Children.Any(static child => child.IsEnabled && child.ControlsContinuation);

        public override bool MayAccessTarget => Children.Any(static child => child.MayAccessTarget);

        protected override void ApplyTo(FilterEffectContext context)
        {
            context.ApplyTransactional(() =>
            {
                foreach (FilterEffect.Resource item in Children)
                {
                    context.Apply(item);
                }
            });
        }
    }
}
