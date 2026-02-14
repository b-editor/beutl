using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Language;

namespace Beutl.Graphics;

[Display(Name = nameof(Strings.Presenter), ResourceType = typeof(Strings))]
public sealed partial class DrawablePresenter : Drawable, IPresenter<Drawable>
{
    public DrawablePresenter()
    {
        ScanProperties<DrawablePresenter>();
    }

    [Display(Name = nameof(Strings.Target), ResourceType = typeof(Strings))]
    public IProperty<Drawable?> Target { get; } = Property.Create<Drawable?>();

    public override void Render(GraphicsContext2D context, Drawable.Resource resource)
    {
        var r = (Resource)resource;
        r.Target?.GetOriginal().Render(context, r.Target);
    }

    protected override void OnDraw(GraphicsContext2D context, Drawable.Resource resource)
    {
    }

    protected override Size MeasureCore(Size availableSize, Drawable.Resource resource)
    {
        var r = (Resource)resource;
        return r.Target?.GetOriginal().MeasureInternal(availableSize, r.Target) ?? Size.Empty;
    }
}
