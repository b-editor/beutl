using System.Reactive;
using System.Reactive.Linq;
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
}
