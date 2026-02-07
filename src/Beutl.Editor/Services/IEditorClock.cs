using Reactive.Bindings;

namespace Beutl.Editor.Services;

public interface IEditorClock
{
    IReactiveProperty<TimeSpan> CurrentTime { get; }

    IReadOnlyReactiveProperty<TimeSpan> MaximumTime { get; }
}
