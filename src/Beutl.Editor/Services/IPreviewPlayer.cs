using System.Reactive;
using System.Reactive.Linq;
using Beutl.Editor.Models;
using Beutl.Media;
using Beutl.Media.Source;
using Reactive.Bindings;

namespace Beutl.Editor.Services;

public interface IPreviewPlayer
{
    IReadOnlyReactiveProperty<Ref<Bitmap>?> PreviewImage { get; }

    IObservable<Unit> AfterRendered { get; }

    IReadOnlyReactiveProperty<bool> IsPlaying { get; }

    IObservable<AudioFrameSnapshot> AudioFramePushed => Observable.Empty<AudioFrameSnapshot>();

    Task<AudioFrameSnapshot?> ComposeAudioAsync(TimeSpan start, TimeSpan duration, CancellationToken ct = default)
        => Task.FromResult<AudioFrameSnapshot?>(null);

    /// <summary>
    /// Pauses playback; the returned task completes once playback can no longer access scene state,
    /// so callers may then safely mutate it (e.g. the scene frame size). A native audio backend may
    /// finish shutting down later, but must not compose or publish into a retired session.
    /// No-op default for players without interactive playback.
    /// </summary>
    Task Pause() => Task.CompletedTask;
}
