using Beutl.Media;

namespace Beutl.Editor.Components.PropertyEditors.ViewModels;

public sealed class AlignmentXEditorViewModel(IPropertyAdapter<AlignmentX> property) : ValueEditorViewModel<AlignmentX>(property)
{
    protected override bool CanEditUnderExpression => true;
}

public sealed class TextAlignmentEditorViewModel(IPropertyAdapter<TextAlignment> property) : ValueEditorViewModel<TextAlignment>(property)
{
    protected override bool CanEditUnderExpression => true;
}
