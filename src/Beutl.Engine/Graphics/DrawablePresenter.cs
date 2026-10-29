using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Language;

namespace Beutl.Graphics;

[Display(Name = nameof(GraphicsStrings.Presenter), ResourceType = typeof(GraphicsStrings))]
public sealed partial class DrawablePresenter : Drawable, IPresenter<Drawable>
{
    public DrawablePresenter()
    {
        ScanProperties<DrawablePresenter>();
    }

    [Display(Name = nameof(GraphicsStrings.Target), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<Drawable?> Target { get; } = Property.CreateReference<Drawable?>();

    public new partial class Resource
    {
        protected override void RenderCore(GraphicsContext2D context)
        {
            Target?.Render(context);
        }

        protected override void OnDraw(GraphicsContext2D context)
        {
        }

        protected override Size MeasureCore(Size availableSize)
        {
            return Target?.Measure(availableSize) ?? Size.Empty;
        }
    }
}
