using Beutl.Graphics;

namespace Beutl.ViewModels.Editors;

public sealed class CurveMapEditorViewModel(IPropertyAdapter<CurveMap> property)
    : ValueEditorViewModel<CurveMap>(property)
{
}
