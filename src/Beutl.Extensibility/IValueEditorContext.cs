using System.Diagnostics.CodeAnalysis;

namespace Beutl.Extensibility;

// The view model side of an editor whose control edits TEdit values; RegisterValue pairs it with IValueEditorControl<TEdit>.
public interface IValueEditorContext<TEdit> : IPropertyEditorContext
{
    // The value the control shows.
    IObservable<TEdit> EditValue { get; }

    // Applies a value the user is still changing and returns the value to show.
    TEdit PreviewEdit(TEdit value);

    // Text entry previews an absolute value; a drag can offset the value below an expression.
    TEdit PreviewEdit(TEdit value, bool relative) => PreviewEdit(value);

    // Applies a complete underlying value entered in a focused box below an expression and returns the value to show.
    TEdit PreviewFocusedEdit(TEdit value) => PreviewEdit(value, relative: false);

    // The underlying value a focused value box shows for editing when an expression is active.
    bool TryGetFocusedEditValue([MaybeNullWhen(false)] out TEdit value)
    {
        value = default;
        return false;
    }

    // The input for the next incremental drag step, which can differ from the expression result on screen.
    TEdit GetIncrementValue(TEdit shown) => shown;

    // Records a change the user finished.
    void ConfirmEdit(TEdit oldValue, TEdit newValue);
}
