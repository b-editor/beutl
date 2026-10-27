using Beutl.Graphics;
using Beutl.Media;
using Beutl.Media.Source;
using Reactive.Bindings;

namespace Beutl.ViewModels;

public partial class PlayerViewModel
{
    private readonly object _cleanPreviewLock = new();
    private volatile bool _cleanPreviewEnabled;
    private int _cleanPreviewConsumers;
    private Size _presentationFrameSize;
    private long _previewPlaybackId;
    private MediaTime _cleanPreviewTime;
    private long _cleanPreviewPlaybackId;

    internal long PreviewPlaybackId => Interlocked.Read(ref _previewPlaybackId);

    internal (Ref<Bitmap>? Image, MediaTime Time, long PlaybackId) GetCleanPreviewFrame()
    {
        lock (_cleanPreviewLock)
            return (CleanPreviewImage.Value?.TryClone(), _cleanPreviewTime, _cleanPreviewPlaybackId);
    }

    public ReactivePropertySlim<Ref<Bitmap>?> CleanPreviewImage { get; } = new();

    internal bool IsCleanPreviewEnabled => _cleanPreviewEnabled;

    internal event EventHandler? CleanPreviewChanged;

    internal IDisposable AcquireCleanPreview()
    {
        UpdateCleanPreviewConsumers(1);
        return Disposable.Create(() => UpdateCleanPreviewConsumers(-1));
    }

    private void UpdateCleanPreviewConsumers(int delta)
    {
        bool changed;
        lock (_cleanPreviewLock)
        {
            _cleanPreviewConsumers = _isDisposing ? 0 : Math.Max(0, _cleanPreviewConsumers + delta);
            bool enabled = _cleanPreviewConsumers > 0;
            changed = _cleanPreviewEnabled != enabled;
            _cleanPreviewEnabled = enabled;
            if (!_cleanPreviewEnabled)
                ReplaceImage(CleanPreviewImage, null);
        }

        if (changed)
            CleanPreviewChanged?.Invoke(this, EventArgs.Empty);

        if (changed && _cleanPreviewEnabled && !IsPlaying.Value)
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
