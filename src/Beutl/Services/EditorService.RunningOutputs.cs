using System.Collections.Immutable;
using Beutl.Editor.Components.OutputTab;
using Reactive.Bindings;

namespace Beutl.Services;

public partial class EditorService : IOutputRunHost
{
    private readonly object _runningOutputsSync = new();
    private readonly ReactivePropertySlim<ImmutableArray<OutputProfileItem>> _runningOutputs = new([]);

    // Every output profile currently running, across all open editors. Published from whichever
    // thread the output runs on, so subscribers must marshal to the UI thread themselves.
    internal IReadOnlyReactiveProperty<ImmutableArray<OutputProfileItem>> RunningOutputs => _runningOutputs;

    IDisposable IOutputRunHost.TrackRunningOutput(OutputProfileItem item) => TrackRunningOutput(item);

    internal IDisposable TrackRunningOutput(OutputProfileItem item)
    {
        lock (_runningOutputsSync)
        {
            _runningOutputs.Value = _runningOutputs.Value.Add(item);
        }

        return Disposable.Create(() =>
        {
            lock (_runningOutputsSync)
            {
                _runningOutputs.Value = _runningOutputs.Value.Remove(item);
            }
        });
    }
}
