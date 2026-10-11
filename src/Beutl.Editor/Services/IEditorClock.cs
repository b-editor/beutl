using Reactive.Bindings;

namespace Beutl.Editor.Services;

public interface IEditorClock
{
    /// <summary>
    /// The playhead of the editor.
    /// </summary>
    /// <remarks>
    /// The editor changes it on the UI thread, during playback as well, so subscribers run on the UI thread and
    /// can update controls and read editor state directly. Set it from the UI thread too.
    /// </remarks>
    IReactiveProperty<TimeSpan> CurrentTime { get; }

    IReadOnlyReactiveProperty<TimeSpan> MaximumTime { get; }
}
