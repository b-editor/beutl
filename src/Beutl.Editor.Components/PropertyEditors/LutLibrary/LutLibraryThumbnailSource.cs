using Avalonia.Media;
using Avalonia.Media.Imaging;
using Beutl.Controls;

namespace Beutl.Editor.Components.PropertyEditors.LutLibrary;

// A still thumbnail for one LUT, rendered when the picker first shows it and kept by the cache. The picker asks
// for it through the animated thumbnail control, so files it never shows are never read.
internal sealed class LutLibraryThumbnailSource(
    string path,
    ILutThumbnailSource thumbnails,
    CancellationToken cancellationToken) : IAnimatedThumbnailSource
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
        LutThumbnailResult result;
        try
        {
            result = await thumbnails.GetAsync(path, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // The picker closed before this tile was drawn; its cache entry was dropped for the next open.
            return;
        }

        if (result.Image == null || cancellationToken.IsCancellationRequested)
            return;

        _still = result.Image;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
