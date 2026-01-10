using Beutl.Media.Source;
using Reactive.Bindings;

namespace Beutl.ViewModels.Editors;

public sealed class ImageSourceEditorViewModel : ValueEditorViewModel<ImageSource?>
{
    public ImageSourceEditorViewModel(IPropertyAdapter<ImageSource?> property)
        : base(property)
    {
        FullName = Value.Select(x => x?.HasUri == true ? x.Uri.LocalPath : null)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);

        FileInfo = FullName.Select(p => p != null ? new FileInfo(p) : null)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);
    }

    public ReadOnlyReactivePropertySlim<string?> FullName { get; }

    public ReadOnlyReactivePropertySlim<FileInfo?> FileInfo { get; }

    public void SetValueAndDispose(ImageSource? oldValue, ImageSource? newValue)
    {
        if (!EqualityComparer<ImageSource?>.Default.Equals(oldValue, newValue))
        {
            if (EditingKeyFrame.Value is { } kf)
            {
                kf.Value = newValue;
            }
            else
            {
                PropertyAdapter.SetValue(newValue);
            }

            Commit();
        }
    }
}
