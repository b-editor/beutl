using Beutl.Graphics;
using Beutl.Media;
using Beutl.Media.Source;
using Reactive.Bindings;

namespace Beutl.ViewModels;

public partial class PlayerViewModel
{
    private readonly object _cleanPreviewLock = new();
    private volatile bool _cleanPreviewEnabled;
    private Size _presentationFrameSize;
    private Size _effectiveFrameSize;

    public ReactivePropertySlim<Ref<Bitmap>?> CleanPreviewImage { get; } = new();

    internal void SetCleanPreviewEnabled(bool enabled)
    {
        lock (_cleanPreviewLock)
        {
            _cleanPreviewEnabled = enabled && !_isDisposing;
            if (!_cleanPreviewEnabled)
            {
                var old = CleanPreviewImage.Value;
                CleanPreviewImage.Value = null;
                old?.Dispose();
            }
        }

        if (_cleanPreviewEnabled && !IsPlaying.Value)
            QueueRender();
    }

    // Ref.Clone retains the same pixels. The editor will paint into its bitmap, so retain a pixel
    // copy for the clean output. Playback already supplies an unmodified frame and shares its ref.
    private Ref<Bitmap>? CopyCleanFrame(Bitmap bitmap)
        => _cleanPreviewEnabled ? Ref<Bitmap>.Create(bitmap.Clone()) : null;

    internal void SetPresentationFrameSize(Size size)
    {
        _presentationFrameSize = size;
        UpdatePreviewSurfaceSize();
    }

    private void UpdatePreviewSurfaceSize()
    {
        // Resizing a window must not replace the renderer/cache underneath active playback.
        if (_isDisposing || IsPlaybackActive || Scene is null)
            return;

        var frame = Scene.FrameSize.Value;
        float Fit(Size size) => MathF.Min(size.Width / frame.Width, size.Height / frame.Height);
        _effectiveFrameSize = frame.Width > 0 && frame.Height > 0
            && Fit(_presentationFrameSize) > Fit(_maxFrameSize)
                ? _presentationFrameSize : _maxFrameSize;
        EditViewModel.PreviewSurfaceSize.Value = _effectiveFrameSize;
        ApplyMaxFrameSizeToCacheOptions(EditViewModel.FrameCacheManager.Value);
    }
}
