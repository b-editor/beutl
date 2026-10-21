using Avalonia;
using Avalonia.Interactivity;

using Beutl.Controls.PropertyEditors;

using Reactive.Bindings;

namespace Beutl.Editor.Components.PropertyEditors.ViewModels;

public sealed class StorageFileEditorViewModel : BaseEditorViewModel<FileInfo>
{
    public StorageFileEditorViewModel(IPropertyAdapter<FileInfo> property)
        : base(property)
    {
        Value = property.GetObservable()
            .ToReadOnlyReactiveProperty()
            .DisposeWith(Disposables);
    }

    public ReadOnlyReactiveProperty<FileInfo?> Value { get; }

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
        if (e.Expect<FileInfo>(sender) is { } args)
        {
            SetValue(Value.Value, args.NewValue);
        }
    }
}
