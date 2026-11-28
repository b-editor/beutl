using Avalonia.Media;
using Avalonia.Media.Imaging;
using Beutl.Controls;

namespace Beutl.Editor.Components.PropertyEditors.LutLibrary;

// A still thumbnail for one LUT, rendered when the picker first shows it and kept by the cache. The picker asks
// for it through the animated thumbnail control, so files it never shows are never read.
internal sealed class LutLibraryThumbnailSource(string path, ILutThumbnailSource thumbnails) : IAnimatedThumbnailSource
{
    private bool _loading;
    private Bitmap? _still;

    public IImage? Still => _still;

    public AnimatedThumbnailFrames? Frames => null;

    public event EventHandler? Changed;

    public IDisposable RequestStill()
    {
        if (_still == null && !_loading)
        {
            _loading = true;
            _ = LoadAsync();
        }

        return Disposable.Empty;
    }

    public IDisposable RequestFrames() => Disposable.Empty;

    private async Task LoadAsync()
    {
        LutThumbnailResult result = await thumbnails.GetAsync(path, CancellationToken.None);
        if (result.Image == null)
            return;

        _still = result.Image;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
