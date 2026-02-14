using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Language;

namespace Beutl.Media;

[Display(Name = nameof(Strings.Presenter), ResourceType = typeof(Strings))]
public sealed partial class BrushPresenter : Brush, IPresenter<Brush>
{
    public BrushPresenter()
    {
        ScanProperties<BrushPresenter>();
    }

    [Display(Name = nameof(Strings.Target), ResourceType = typeof(Strings))]
    public IProperty<Brush?> Target { get; } = Property.Create<Brush?>();
}
