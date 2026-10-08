using Avalonia;
using Avalonia.Interactivity;
using Beutl.Controls.PropertyEditors;
using Beutl.IO;
using Beutl.Media.Source;
using Beutl.Services;
using Reactive.Bindings;

namespace Beutl.ViewModels.Editors;

public sealed class FileSourceEditorViewModel<T> : ValueEditorViewModel<T?>
    where T : class, IFileSource, new()
{
    private readonly HashSet<StorageFileEditor> _editors = [];

    public FileSourceEditorViewModel(IPropertyAdapter<T?> property)
        : base(property)
    {
        FileInfo = Value.Select(GetFileInfo)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);
    }

    public ReadOnlyReactivePropertySlim<FileInfo?> FileInfo { get; }

    public override void Accept(IPropertyEditorContextVisitor visitor)
    {
        base.Accept(visitor);
        if (visitor is not StorageFileEditor editor || IsDisposed || !_editors.Add(editor)) return;

        editor.Bind(StorageFileEditor.ValueProperty, FileInfo.ToBinding())
            .DisposeWith(Disposables);
        editor.AddDisposableHandler(PropertyEditor.ValueConfirmedEvent, (_, e) => OnValueConfirmed(editor, e))
            .DisposeWith(Disposables);
    }

    private void OnValueConfirmed(StorageFileEditor editor, PropertyEditorValueChangedEventArgs e)
    {
        if (e is not PropertyEditorValueChangedEventArgs<FileInfo?> args) return;
        if (!CanEdit.Value)
        {
            editor.Value = FileInfo.Value;
            return;
        }

        T? source = null;
        try
        {
            if (args.NewValue is { } file)
            {
                source = new T();
                source.ReadFrom(new Uri(file.FullName));
            }
        }
        catch (Exception ex)
        {
            editor.Value = FileInfo.Value;
            NotificationService.ShowError(MessageStrings.OperationFailed, ex.Message);
            return;
        }

        SetValue(Value.Value, source);
        editor.Value = FileInfo.Value;
    }

    private static FileInfo? GetFileInfo(T? source)
    {
        if (source is null or MediaSource { HasUri: false }) return null;

        try
        {
            return source.Uri is { IsAbsoluteUri: true, IsFile: true } uri ? new FileInfo(uri.LocalPath) : null;
        }
        catch (InvalidOperationException)
        {
            // IFileSource implementations may throw until their URI has been initialized.
            return null;
        }
    }
}
