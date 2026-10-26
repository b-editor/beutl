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

    public ReactivePropertySlim<Ref<Bitmap>?> CleanPreviewImage { get; } = new();

    internal bool IsCleanPreviewEnabled => _cleanPreviewEnabled;

    internal event EventHandler? CleanPreviewChanged;

    internal void SetCleanPreviewEnabled(bool enabled)
    {
        bool changed;
        lock (_cleanPreviewLock)
        {
            enabled &= !_isDisposing;
            changed = _cleanPreviewEnabled != enabled;
            _cleanPreviewEnabled = enabled;
            if (!_cleanPreviewEnabled)
                ReplaceImage(CleanPreviewImage, null);
        }

        if (changed)
            CleanPreviewChanged?.Invoke(this, EventArgs.Empty);

        if (_cleanPreviewEnabled && !IsPlaying.Value)
            QueueRender();
    }

    // Ref.Clone retains the same pixels. The editor will paint into its bitmap, so retain a pixel
    // copy for the clean output. Playback shares its ref unless a 3D gizmo requires a separate image.
    private Ref<Bitmap>? CopyCleanFrame(SceneRenderer renderer, Bitmap bitmap)
        => _cleanPreviewEnabled
            ? Ref<Bitmap>.Create(renderer.SnapshotWithoutGizmos() ?? bitmap.Clone())
            : null;

    internal Ref<Bitmap>? CreatePlaybackCleanFrame(SceneRenderer renderer, Ref<Bitmap> bitmap, MediaTime? cachedTime = null)
    {
        if (!_cleanPreviewEnabled)
            return null;

        if (cachedTime.HasValue)
            renderer.UpdateFrame(renderer.Compositor.EvaluateGraphics(cachedTime.Value));
        return renderer.SnapshotWithoutGizmos() is { } clean
            ? Ref<Bitmap>.Create(clean)
            : bitmap.Clone();
    }

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
        EditViewModel.PreviewSurfaceSize.Value = frame.Width > 0 && frame.Height > 0
            && Fit(_presentationFrameSize) > Fit(_maxFrameSize)
                ? _presentationFrameSize : _maxFrameSize;
        ApplyMaxFrameSizeToCacheOptions(EditViewModel.FrameCacheManager.Value);
    }
}
