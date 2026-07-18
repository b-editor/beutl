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
    public IProperty<FilterEffect?> Target { get; } = Property.Create<FilterEffect?>();

    public override void Describe(EffectGraphBuilder builder, FilterEffect.Resource resource)
    {
        var r = (Resource)resource;
        if (r.Target is { IsEnabled: true } target)
            builder.Effect(target);
    }
}
