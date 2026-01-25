using Beutl.Engine;
using Beutl.Graphics.Rendering;

namespace Beutl.Media;

public sealed partial class BrushPresenter : Brush, IPresenter<Brush>
{
    public BrushPresenter()
    {
        ScanProperties<BrushPresenter>();
    }

    public IProperty<Brush?> Target { get; } = Property.Create<Brush?>();
}
