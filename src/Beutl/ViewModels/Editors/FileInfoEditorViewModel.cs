using Avalonia;
using Avalonia.Platform.Storage;
using Avalonia.Platform.Storage.FileIO;

using Beutl.Controls.PropertyEditors;
using Beutl.Framework;

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
        if (visitor is StorageFileEditor editor)
        {
            editor[!StorageFileEditor.ValueProperty] = Value.ToBinding();
            editor.ValueChanged += OnValueChanged;
        }
    }

    private void OnValueChanged(object? sender, PropertyEditorValueChangedEventArgs e)
    {
        if (e is PropertyEditorValueChangedEventArgs<FileInfo> args)
        {
            SetValue(Value.Value, args.NewValue);
        }
    }
}
