using Avalonia;
using Avalonia.Interactivity;

using Beutl.Controls.PropertyEditors;
using Beutl.Media;

namespace Beutl.ViewModels.Editors;

public sealed class AlignmentYEditorViewModel(IAbstractProperty<AlignmentY> property) : ValueEditorViewModel<AlignmentY>(property)
{
    public override void Accept(IPropertyEditorContextVisitor visitor)
    {
        base.Accept(visitor);
        if (visitor is AlignmentYEditor view && !Disposables.IsDisposed)
        {
            view.Bind(AlignmentYEditor.ValueProperty, Value.ToBinding())
                .DisposeWith(Disposables);
            view.AddDisposableHandler(PropertyEditor.ValueConfirmedEvent, OnValueConfirmed)
                .DisposeWith(Disposables);
        }
    }

    private void OnValueConfirmed(object? sender, PropertyEditorValueChangedEventArgs e)
    {
        if (e is PropertyEditorValueChangedEventArgs<AlignmentY> args)
        {
            SetValue(args.OldValue, args.NewValue);
        }
    }
}
