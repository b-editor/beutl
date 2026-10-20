using Beutl.AgentToolkit.Sessions;
using Beutl.Editor;
using Beutl.ProjectSystem;

namespace Beutl.AgentToolkit.Tests.Helpers;

internal sealed class FakeLiveBinding(Scene? scene, HistoryManager? history) : ILiveSessionBinding
{
    public int InvokeCount { get; private set; }

    public bool Alive { get; set; } = true;

    public Scene? ActiveScene { get; } = scene;

    public HistoryManager? ActiveHistory { get; } = history;

    public bool IsAlive => Alive && ActiveScene is not null && ActiveHistory is not null;

    public void Invoke(Action action)
    {
        InvokeCount++;
        action();
    }

    public ValueTask<TResult> ExecuteHistoryMutationAsync<TResult>(
        Func<HistoryManager, bool> shouldPause,
        Func<HistoryManager, TResult> operation,
        CancellationToken cancellationToken = default,
        TimeSpan? sessionWait = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        TResult result = default!;
        Invoke(() =>
        {
            HistoryManager activeHistory = ActiveHistory ?? throw new SessionUnavailableException();
            _ = shouldPause(activeHistory);
            activeHistory.FlushPendingMutations();
            result = operation(activeHistory);
        });
        return ValueTask.FromResult(result);
    }
}
