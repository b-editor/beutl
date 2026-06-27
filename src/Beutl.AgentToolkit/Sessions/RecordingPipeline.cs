using Beutl.Editor;
using Beutl.Editor.Observers;

namespace Beutl.AgentToolkit.Sessions;

public sealed class RecordingPipeline : IDisposable
{
    private readonly IDisposable _historySubscription;
    private readonly CoreObjectOperationObserver _observer;

    private RecordingPipeline(
        OperationSequenceGenerator sequence,
        HistoryManager history,
        CoreObjectOperationObserver observer,
        IDisposable historySubscription)
    {
        Sequence = sequence;
        History = history;
        _observer = observer;
        _historySubscription = historySubscription;
    }

    public OperationSequenceGenerator Sequence { get; }

    public HistoryManager History { get; }

    public static RecordingPipeline Create(CoreObject root)
    {
        ArgumentNullException.ThrowIfNull(root);

        var sequence = new OperationSequenceGenerator();
        var history = new HistoryManager(root, sequence);
        var observer = new CoreObjectOperationObserver(null, root, sequence);
        IDisposable subscription = history.Subscribe(observer);
        return new RecordingPipeline(sequence, history, observer, subscription);
    }

    public void Dispose()
    {
        _historySubscription.Dispose();
        _observer.Dispose();
        History.Dispose();
    }
}
