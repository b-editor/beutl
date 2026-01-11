using Beutl.Graphics;
using Beutl.Graphics.Effects;

namespace Beutl.ViewModels.Editors;

public sealed class CurveMapEditorViewModel(IPropertyAdapter<CurveMap> property)
    : ValueEditorViewModel<CurveMap>(property)
{
    public Curves? TryGetCurves()
    {
        return PropertyAdapter.GetEngineProperty()?.GetOwnerObject() as Curves;
    }
}
