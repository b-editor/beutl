using Beutl.Editor;
using Beutl.ProjectSystem;

namespace Beutl.AgentToolkit.Sessions;

public interface ILiveSessionBinding
{
    Scene? ActiveScene { get; }

    HistoryManager? ActiveHistory { get; }

    bool IsAlive { get; }

    void Invoke(Action action);

    /// <summary>
    /// Executes one synchronous history batch through the live editor's playback guard.
    /// </summary>
    /// <remarks>
    /// Implementations must serialize with editor history commands, evaluate
    /// <paramref name="shouldPause"/> before flushing pending mutations, pause and drain active
    /// playback when needed, flush pending mutations, and run <paramref name="operation"/> on the
    /// editor thread before releasing the shared guard.
    /// </remarks>
    ValueTask<TResult> ExecuteHistoryMutationAsync<TResult>(
        Func<HistoryManager, bool> shouldPause,
        Func<HistoryManager, TResult> operation,
        CancellationToken cancellationToken = default);
}
