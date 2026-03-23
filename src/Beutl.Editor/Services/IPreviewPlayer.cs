using System.Reactive;
using Beutl.Media;
using Beutl.Media.Source;

using Reactive.Bindings;

namespace Beutl.Editor.Services;

public interface IPreviewPlayer
{
    IReadOnlyReactiveProperty<Ref<Bitmap>?> PreviewImage { get; }

    IObservable<Unit> AfterRendered { get; }

    IReadOnlyReactiveProperty<bool> IsPlaying { get; }
}
