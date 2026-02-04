using System.Reactive;

using Avalonia.Media;
using Reactive.Bindings;

namespace Beutl.Editor.Services;

public interface IPreviewPlayer
{
    IReadOnlyReactiveProperty<IImage?> PreviewImage { get; }

    IObservable<Unit> AfterRendered { get; }

    IReadOnlyReactiveProperty<bool> IsPlaying { get; }
}
