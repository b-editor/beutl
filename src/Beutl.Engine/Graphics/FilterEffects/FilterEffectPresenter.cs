using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Language;

namespace Beutl.Graphics.Effects;

[Display(Name = nameof(GraphicsStrings.Presenter), ResourceType = typeof(GraphicsStrings))]
public sealed partial class FilterEffectPresenter : FilterEffect, IPresenter<FilterEffect>
{
    public FilterEffectPresenter()
    {
        ScanProperties<FilterEffectPresenter>();
    }

    [Display(Name = nameof(GraphicsStrings.Target), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<FilterEffect?> Target { get; } = Property.CreateReference<FilterEffect?>();

    public partial class Resource
    {
        public override bool ControlsContinuation => Target is { IsEnabled: true, ControlsContinuation: true };

        public override bool MayAccessTarget => Target?.MayAccessTarget ?? false;

        protected override void ApplyTo(FilterEffectContext context)
        {
            // Apply skips a disabled target, matching ControlsContinuation, which a disabled target does not declare.
            if (Target is { } target)
                context.Apply(target);
        }
    }
}
