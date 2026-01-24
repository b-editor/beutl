using Beutl.Engine;
using Beutl.Graphics.Rendering;

namespace Beutl.Graphics.Effects;

public sealed partial class FilterEffectPresenter : FilterEffect, IPresenter<FilterEffect>
{
    public FilterEffectPresenter()
    {
        ScanProperties<FilterEffectPresenter>();
    }

    public IProperty<FilterEffect?> Target { get; } = Property.Create<FilterEffect?>();

    public override void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource)
    {
        var r = (Resource)resource;

        r.Target?.GetOriginal().ApplyTo(context, r.Target);
    }
}
