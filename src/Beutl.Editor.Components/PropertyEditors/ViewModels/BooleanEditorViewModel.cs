namespace Beutl.Editor.Components.PropertyEditors.ViewModels;

public sealed class BooleanEditorViewModel(IPropertyAdapter<bool> property) : ValueEditorViewModel<bool>(property)
{
    protected override bool CanEditUnderExpression => true;
}
