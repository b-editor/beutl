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

        File = Value.Select(x => (IStorageFile?)(x != null ? new BclStorageFile(x) : null))
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);
    }

    public ReadOnlyReactivePropertySlim<FileInfo?> Value { get; }

    public ReadOnlyReactivePropertySlim<IStorageFile?> File { get; }

    public override void Accept(IPropertyEditorContextVisitor visitor)
    {
        base.Accept(visitor);
        if (visitor is StorageFileEditor editor)
        {
            editor[!StorageFileEditor.ValueProperty] = File.ToBinding();
            editor.ValueChanged += OnValueChanged;
        }
    }

    private void OnValueChanged(object? sender, PropertyEditorValueChangedEventArgs e)
    {
        if (e is PropertyEditorValueChangedEventArgs<IStorageFile> args
            && args.NewValue.Path is { IsFile: true } uri)
        {
            SetValue(Value.Value, new FileInfo(uri.LocalPath));
            args.NewValue.Dispose();
        }
    }
}
