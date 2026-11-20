namespace Beutl.Editor.Components.PropertyEditors.ViewModels;

public sealed class RationalEditorViewModel(IPropertyAdapter<Rational> property) : ValueEditorViewModel<Rational>(property)
{
    protected override bool CanEditUnderExpression => true;

    protected override Rational OffsetValue(Rational value, Rational from, Rational to) => value + (to - from);
}
