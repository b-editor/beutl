using Avalonia;
using Avalonia.Interactivity;

using Beutl.Controls.PropertyEditors;

namespace Beutl.ViewModels.Editors;

public sealed class EnumEditorViewModel<T>(IAbstractProperty<T> property) : ValueEditorViewModel<T>(property)
    where T : struct, Enum
{
    public override void Accept(IPropertyEditorContextVisitor visitor)
    {
        base.Accept(visitor);
        if (visitor is EnumEditor<T> editor && !Disposables.IsDisposed)
        {
            editor.Bind(EnumEditor<T>.SelectedValueProperty, Value.ToBinding())
                .DisposeWith(Disposables);
            editor.AddDisposableHandler(PropertyEditor.ValueConfirmedEvent, OnValueConfirmed)
                .DisposeWith(Disposables);
        }
    }

    private void OnValueConfirmed(object? sender, PropertyEditorValueChangedEventArgs e)
    {
        if (e is PropertyEditorValueChangedEventArgs<T> args)
        {
            SetValue(Value.Value, args.NewValue);
        }
    }
}
