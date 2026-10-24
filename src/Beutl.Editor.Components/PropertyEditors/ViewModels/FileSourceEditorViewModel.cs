using Beutl.Editor.PropertyEditors;
using Beutl.IO;
using Beutl.Media.Source;
using Beutl.Serialization;
using Beutl.Services;
using Reactive.Bindings;

namespace Beutl.Editor.Components.PropertyEditors.ViewModels;

public sealed class FileSourceEditorViewModel<T> : PropertyEditorViewModel<T?, FileInfo?>
    where T : class, IFileSource, new()
{
    public FileSourceEditorViewModel(IPropertyAdapter<T?> property)
        : base(property)
    {
        FileInfo = ShownValue.Select(GetFileInfo)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);
    }

    public ReadOnlyReactivePropertySlim<FileInfo?> FileInfo { get; }

    protected override FileInfo? ToEdit(T? value) => GetFileInfo(value);

    // Reading the file can throw; ConfirmEdit reports that.
    protected override T? FromEdit(FileInfo? edit)
    {
        if (edit is null) return null;

        var source = new T();
        source.ReadFrom(UriHelper.CreateFromPath(edit.FullName));
        return source;
    }

    public override void ConfirmEdit(FileInfo? oldValue, FileInfo? newValue)
    {
        if (IsDisposed || !CanEdit.Value) return;

        T? source;
        try
        {
            source = FromEdit(newValue);
        }
        catch (Exception ex)
        {
            NotificationService.ShowError(MessageStrings.OperationFailed, ex.Message);
            return;
        }

        SetValue(ShownValue.Value, source);
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
