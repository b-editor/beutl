using System.Reactive.Threading.Tasks;
using Beutl.Editor.Services;
using Beutl.Editor.Services.LibraryThumbnails;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.ProjectSystem;

namespace Beutl.Editor.Components.LibraryThumbnails;

// The frame the preview has drawn, without what the editor draws over it, as the library's thumbnails show types on
// it. Nothing is drawn again for it: the frame is read while the library holds the preview's clean frame. It
// remembers the frame it took, so a frame the thumbnails already show is not taken again. Used on the UI thread.
internal sealed class LibraryPreviewCapture
{
    // How long a first capture waits for the preview to draw its clean frame, which it starts on being held.
    private static readonly TimeSpan s_firstFrameWait = TimeSpan.FromSeconds(2);

    private readonly Scene _scene;
    // Only compared with, so the frame is not kept for it.
    private WeakReference<Bitmap>? _taken;

    private LibraryPreviewCapture(IPreviewPlayer player, Scene scene)
    {
        Player = player;
        _scene = scene;
    }

    public IPreviewPlayer Player { get; }

    // The capture for the editor's preview, or null without a preview to read it from.
    public static LibraryPreviewCapture? Create(IServiceProvider? editor)
    {
        return editor?.GetService(typeof(IPreviewPlayer)) is IPreviewPlayer player
               && editor.GetService(typeof(Scene)) is Scene scene
            ? new LibraryPreviewCapture(player, scene)
            : null;
    }

    // Whether the thumbnails were not captured from `frame`, such as one the preview drew after an edit.
    public bool IsNew(Ref<Bitmap> frame)
    {
        return frame.Value is not { } bitmap
               || _taken?.TryGetTarget(out Bitmap? taken) != true
               || !ReferenceEquals(taken, bitmap);
    }

    public async Task<LibraryThumbnailSubject?> CaptureAsync(CancellationToken cancellationToken)
    {
        Ref<Bitmap>? frame = await TakeCleanFrameAsync(cancellationToken);
        _taken = frame == null ? null : new WeakReference<Bitmap>(frame.Value);
        return await LibraryThumbnailSubject.FromFrameAsync(frame, _scene.FrameSize.Value, cancellationToken);
    }

    // The clean frame the preview has, or the first it draws; null when it draws none in time, which leaves the
    // samples.
    private async Task<Ref<Bitmap>?> TakeCleanFrameAsync(CancellationToken cancellationToken)
    {
        if (Player.CleanPreviewImage.Value?.TryClone() is { } current)
            return current;

        try
        {
            Ref<Bitmap>? drawn = await Player.CleanPreviewImage
                .Where(frame => frame != null)
                .FirstAsync()
                .Timeout(s_firstFrameWait)
                .ToTask(cancellationToken);
            return drawn?.TryClone();
        }
        catch (TimeoutException)
        {
            return null;
        }
    }
}
