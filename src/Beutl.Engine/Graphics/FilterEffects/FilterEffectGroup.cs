namespace Beutl.Graphics.Effects;

public sealed partial class FilterEffectGroup : FilterEffect
{
    public FilterEffectGroup()
    {
        Children = new FilterEffects(this);
        Children.Invalidated += (_, e) => RaiseInvalidated(e);
    }

    public FilterEffects Children { get; }

    public override void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource)
    {
        var r = (Resource)resource;
        foreach (FilterEffect.Resource item in r.Children)
        {
            item.GetOriginal().ApplyTo(context, item);
        }
    }
}
