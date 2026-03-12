using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Language;

namespace Beutl.Media;

[Display(Name = nameof(GraphicsStrings.Presenter), ResourceType = typeof(GraphicsStrings))]
public sealed partial class BrushPresenter : Brush, IPresenter<Brush>
{
    public BrushPresenter()
    {
        ScanProperties<BrushPresenter>();
    }

    [Display(Name = nameof(GraphicsStrings.Target), ResourceType = typeof(GraphicsStrings))]
    public IProperty<Brush?> Target { get; } = Property.Create<Brush?>();
}
