using System.Reactive.Linq;
using Beutl.Extensibility;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.Editor.PropertyEditors;

// A view model whose control edits TEdit values, such as a pair of numbers for a point.
// RegisterValue pairs it with a control that implements IValueEditorControl<TEdit>.
public abstract class PropertyEditorViewModel<TValue, TEdit> : PropertyEditorViewModel<TValue>, IValueEditorContext<TEdit>
{
    private ReadOnlyReactiveProperty<TEdit>? _editValue;

    protected PropertyEditorViewModel(IPropertyAdapter<TValue> property)
        : base(property)
    {
    }

    public IObservable<TEdit> EditValue => _editValue ??= ShownValue
        .Select(value => ToEdit(value))
        .ToReadOnlyReactiveProperty()
        .AddTo(Disposables)!;

    // The value the control edits for a value of the property.
    protected abstract TEdit ToEdit(TValue value);

    // The value of the property that an edit stands for.
    protected abstract TValue FromEdit(TEdit edit);

    public virtual TEdit PreviewEdit(TEdit value)
    {
        if (IsDisposed) return value;
        return ToEdit(SetCurrentValueAndGetCoerced(FromEdit(value))!);
    }

    public virtual void ConfirmEdit(TEdit oldValue, TEdit newValue)
    {
        SetValue(FromEdit(oldValue), FromEdit(newValue));
    }

    internal override bool TryConnect(object control)
    {
        if (control is not IValueEditorControl<TEdit> valueControl) return false;

        valueControl.Connect(this);
        return true;
    }
}
