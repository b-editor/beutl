using Avalonia;
using Avalonia.Interactivity;

using Beutl.Controls.PropertyEditors;

using Reactive.Bindings;

namespace Beutl.ViewModels.Editors;

public sealed class StorageFileEditorViewModel : BaseEditorViewModel<FileInfo>
{
    public StorageFileEditorViewModel(IAbstractProperty<FileInfo> property)
        : base(property)
    {
        Value = property.GetObservable()
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);
    }

    public ReadOnlyReactivePropertySlim<FileInfo?> Value { get; }

    public override void Accept(IPropertyEditorContextVisitor visitor)
    {
        base.Accept(visitor);
        if (visitor is StorageFileEditor editor && !Disposables.IsDisposed)
        {
            editor.Bind(StorageFileEditor.ValueProperty, Value.ToBinding())
                .DisposeWith(Disposables);
            editor.AddDisposableHandler(PropertyEditor.ValueConfirmedEvent, OnValueConfirmed)
                .DisposeWith(Disposables);
        }
    }

    private void OnValueConfirmed(object? sender, PropertyEditorValueChangedEventArgs e)
    {
        if (e is PropertyEditorValueChangedEventArgs<FileInfo> args)
        {
            SetValue(Value.Value, args.NewValue);
        }
    }
}
