using Avalonia;
using Avalonia.Interactivity;

using Beutl.Controls.PropertyEditors;

namespace Beutl.ViewModels.Editors;

public sealed class BooleanEditorViewModel(IAbstractProperty<bool> property) : ValueEditorViewModel<bool>(property)
{
    public override void Accept(IPropertyEditorContextVisitor visitor)
    {
        base.Accept(visitor);
        if (visitor is BooleanEditor view && !Disposables.IsDisposed)
        {
            view.Bind(BooleanEditor.ValueProperty, Value.ToBinding())
                .DisposeWith(Disposables);
            view.AddDisposableHandler(PropertyEditor.ValueConfirmedEvent, OnValueConfirmed)
                .DisposeWith(Disposables);
        }
    }

    private void OnValueConfirmed(object? sender, PropertyEditorValueChangedEventArgs e)
    {
        if (e is PropertyEditorValueChangedEventArgs<bool> args)
        {
            SetValue(args.OldValue, args.NewValue);
        }
    }
}
