using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Beutl.Editor.Models;
using Beutl.Media;
using Beutl.Media.Source;
using Reactive.Bindings;

namespace Beutl.Editor.Services;

public interface IPreviewPlayer
{
    IReadOnlyReactiveProperty<Ref<Bitmap>?> PreviewImage { get; }

    /// <summary>
    /// Gets the frame the preview shows without what the editor draws over it, such as the selection's outline,
    /// onion skins and 3D gizmos, while <see cref="AcquireCleanPreview"/> is held. A player that draws nothing over
    /// its frames gives <see cref="PreviewImage"/>.
    /// </summary>
    IReadOnlyReactiveProperty<Ref<Bitmap>?> CleanPreviewImage => PreviewImage;

    /// <summary>
    /// Keeps <see cref="CleanPreviewImage"/> up to date until the returned handle is disposed. Copying each frame
    /// clean costs the preview a little, so it is held only while something shows it.
    /// </summary>
    IDisposable AcquireCleanPreview() => Disposable.Empty;

    IObservable<Unit> AfterRendered { get; }

    IReadOnlyReactiveProperty<bool> IsPlaying { get; }

    IObservable<AudioFrameSnapshot> AudioFramePushed => Observable.Empty<AudioFrameSnapshot>();

    Task<AudioFrameSnapshot?> ComposeAudioAsync(MediaTime start, MediaTime duration, CancellationToken ct = default)
        => Task.FromResult<AudioFrameSnapshot?>(null);

    /// <summary>
    /// Starts playback from the playhead. Does nothing while it is already playing, or in a player without
    /// interactive playback.
    /// </summary>
    void Play()
    {
    }

    /// <summary>
    /// Gets where playback is now, or <see langword="null"/> while it is not playing.
    /// </summary>
    /// <remarks>
    /// This follows the audio the user hears, and the wall clock when nothing is playing audio. Unlike the
    /// editor clock's current time, which moves when a rendered frame is shown, it does not step from frame to
    /// frame or fall behind a slow render. While playback is starting, and during fast or reverse playback,
    /// which plays no audio, it is the playhead.
    /// </remarks>
    MediaTime? GetPlaybackTime() => null;

    /// <summary>
    /// Pauses playback; the returned task completes once playback can no longer access scene state,
    /// so callers may then safely mutate it (e.g. the scene frame size). A native audio backend may
    /// finish shutting down later, but must not compose or publish into a retired session.
    /// No-op default for players without interactive playback.
    /// </summary>
    Task Pause() => Task.CompletedTask;
}
