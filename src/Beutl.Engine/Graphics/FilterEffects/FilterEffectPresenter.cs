using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Language;

namespace Beutl.Graphics.Effects;

[Display(Name = nameof(Strings.Presenter), ResourceType = typeof(Strings))]
public sealed partial class FilterEffectPresenter : FilterEffect, IPresenter<FilterEffect>
{
    public FilterEffectPresenter()
    {
        ScanProperties<FilterEffectPresenter>();
    }

    [Display(Name = nameof(Strings.Target), ResourceType = typeof(Strings))]
    public IProperty<FilterEffect?> Target { get; } = Property.Create<FilterEffect?>();

    public override void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource)
    {
        var r = (Resource)resource;

        r.Target?.GetOriginal().ApplyTo(context, r.Target);
    }
}
