namespace Beutl.Extensibility;

// The view model side of an editor whose control edits TEdit values; RegisterValue pairs it with IValueEditorControl<TEdit>.
public interface IValueEditorContext<TEdit> : IPropertyEditorContext
{
    // The value the control shows.
    IObservable<TEdit> EditValue { get; }

    // Applies a value the user is still changing and returns the value the property took.
    TEdit PreviewEdit(TEdit value);

    // Records a change the user finished.
    void ConfirmEdit(TEdit oldValue, TEdit newValue);
}
