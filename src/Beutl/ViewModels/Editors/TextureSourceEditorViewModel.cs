using Beutl.Graphics3D.Textures;
using Reactive.Bindings;

namespace Beutl.ViewModels.Editors;

public sealed class TextureSourceEditorViewModel : ValueEditorViewModel<TextureSource?>
{
    public TextureSourceEditorViewModel(IPropertyAdapter<TextureSource?> property)
        : base(property)
    {
        FullName = Value.Select(GetFilePath)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);

        FileInfo = FullName.Select(p => p != null ? new FileInfo(p) : null)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);
    }

    public ReadOnlyReactivePropertySlim<string?> FullName { get; }

    public ReadOnlyReactivePropertySlim<FileInfo?> FileInfo { get; }

    private static string? GetFilePath(TextureSource? source)
    {
        if (source is ImageTextureSource imageSource)
        {
            var imgSource = imageSource.Source.CurrentValue;
            if (imgSource?.HasUri == true)
            {
                return imgSource.Uri.LocalPath;
            }
        }
        return null;
    }

    public void SetValueAndDispose(TextureSource? oldValue, TextureSource? newValue)
    {
        if (!EqualityComparer<TextureSource?>.Default.Equals(oldValue, newValue))
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
