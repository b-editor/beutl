using System.Diagnostics.CodeAnalysis;
using System.Reactive.Linq;
using Beutl.Extensibility;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.Editor.PropertyEditors;

// A view model whose control edits TEdit values, such as a pair of numbers for a point.
// RegisterValue pairs it with a control that implements IValueEditorControl<TEdit>.
public abstract class PropertyEditorViewModel<TValue, TEdit> : PropertyEditorViewModel<TValue>, IValueEditorContext<TEdit>
{
    private ReadOnlyReactivePropertySlim<TEdit>? _editValue;

    protected PropertyEditorViewModel(IPropertyAdapter<TValue> property)
        : base(property)
    {
    }

    public IObservable<TEdit> EditValue => _editValue ??= ShownValue
        .Select(value => ToEdit(value))
        .ToReadOnlyReactivePropertySlim()
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

    public virtual TEdit PreviewEdit(TEdit value, bool relative)
        => relative || !HasExpression.Value ? PreviewEdit(value)
            : ToEdit(SetCurrentValueAndGetCoerced(FromEdit(value), relative: false)!);

    public virtual TEdit PreviewEdit(TEdit value, bool relative, int editedComponents)
        => relative || !HasExpression.Value ? PreviewEdit(value, relative)
            : ToEdit(SetCurrentValueAndGetCoerced(FromEdit(value), relative: false, editedComponents: editedComponents)!);

    public TEdit PreviewFocusedEdit(TEdit value)
        => !HasExpression.Value ? PreviewEdit(value)
            : ToEdit(SetCurrentValueAndGetCoerced(FromEdit(value), relative: false, directUnderlying: true)!);

    public bool TryGetFocusedEditValue([MaybeNullWhen(false)] out TEdit value)
    {
        if (!IsDisposed && HasExpression.Value && CanEditUnderExpression)
        {
            value = ToEdit(GetEditingValue()!);
            return true;
        }

        value = default;
        return false;
    }

    public TEdit GetIncrementValue(TEdit shown) => ToEdit(base.GetIncrementValue(FromEdit(shown))!);

    public virtual void ConfirmEdit(TEdit oldValue, TEdit newValue)
    {
        if (IsDisposed) return;
        SetValue(FromEdit(oldValue), FromEdit(newValue));
    }

    internal override bool TryConnect(object control)
    {
        if (control is not IValueEditorControl<TEdit> valueControl) return false;

        valueControl.Connect(this);
        return true;
    }
}
