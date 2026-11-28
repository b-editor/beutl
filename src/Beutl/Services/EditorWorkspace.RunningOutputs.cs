using System.Collections.Immutable;
using Beutl.Editor.Components.OutputTab;
using Reactive.Bindings;

namespace Beutl.Services;

internal sealed partial class EditorWorkspace : IOutputRunHost
{
    private readonly object _runningOutputsSync = new();
    private readonly ReactivePropertySlim<ImmutableArray<OutputProfileItem>> _runningOutputs = new([]);

    // The output profiles running in this project's editors. Published from whichever thread the output runs on,
    // so subscribers must marshal to the UI thread themselves.
    internal IReadOnlyReactiveProperty<ImmutableArray<OutputProfileItem>> RunningOutputs => _runningOutputs;

    IDisposable IOutputRunHost.TrackRunningOutput(OutputProfileItem item) => TrackRunningOutput(item);

    // The output also counts among the running outputs of the editor service, which shows those of every project.
    internal IDisposable TrackRunningOutput(OutputProfileItem item)
    {
        lock (_runningOutputsSync)
        {
            _runningOutputs.Value = _runningOutputs.Value.Add(item);
        }

        IDisposable shown = Owner.TrackRunningOutputOfAnyProject(item);
        return Disposable.Create(() =>
        {
            lock (_runningOutputsSync)
            {
                _runningOutputs.Value = _runningOutputs.Value.Remove(item);
            }

            shown.Dispose();
        });
    }

    private bool HasRunningOutputs
    {
        get
        {
            lock (_runningOutputsSync)
            {
                return !_runningOutputs.Value.IsEmpty;
            }
        }
    }
}
