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
    }

    public override void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource)
    {
        var r = (Resource)resource;

        // Apply skips a disabled target, matching ControlsContinuation, which a disabled target does not declare.
        if (r.Target is { } target)
            context.Apply(target);
    }
}
