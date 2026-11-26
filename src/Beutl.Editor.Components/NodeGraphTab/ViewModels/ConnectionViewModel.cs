using Avalonia;
using Beutl.Editor.Services;
using Beutl.NodeGraph;
using Microsoft.Extensions.DependencyInjection;
using Reactive.Bindings;

namespace Beutl.Editor.Components.NodeGraphTab.ViewModels;

public class ConnectionViewModel : IDisposable
{
    private readonly CompositeDisposable _disposables = new();
    private readonly NodeGraphViewModel _nodeGraph;

    public ConnectionViewModel(NodeGraphViewModel nodeGraph, Connection connection)
    {
        _nodeGraph = nodeGraph;
        Connection = connection;
        FallbackInput = nodeGraph.NodeGraph.FindSavedFallbackPort(connection, input: true)?.Node;
        FallbackOutput = nodeGraph.NodeGraph.FindSavedFallbackPort(connection, input: false)?.Node;
        InputPortVM = connection.Input.GetObservable()
            .Select(i => FallbackInput == null && i.Value is IInputPort input
                ? _nodeGraph.FindNodePortViewModel(input) as InputPortViewModel
                : null)
            .ToReactiveProperty()
            .DisposeWith(_disposables);
        OutputPortVM = connection.Output.GetObservable()
            .Select(i => FallbackOutput == null && i.Value is IOutputPort output
                ? _nodeGraph.FindNodePortViewModel(output) as OutputPortViewModel
                : null)
            .ToReactiveProperty()
            .DisposeWith(_disposables);
        Status = connection.Status.GetObservable()
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        TrackPortMembership(InputPortVM);
        TrackPortMembership(OutputPortVM);
    }

    // Keeps this connection listed on whichever port view model it currently attaches to.
    private void TrackPortMembership<T>(ReactiveProperty<T?> port)
        where T : NodePortViewModel
    {
        port.CombineWithPrevious()
            .Subscribe(tuple =>
            {
                if (tuple.OldValue != null)
                {
                    tuple.OldValue.Connections.Remove(this);
                }

                if (tuple.NewValue != null && !tuple.NewValue.Connections.Contains(this))
                {
                    int index = tuple.NewValue.GetInsertionIndex(Connection.Id.Value);
                    tuple.NewValue.Connections.Insert(index, this);
                }
            })
            .DisposeWith(_disposables);
    }

    public Connection Connection { get; }

    internal FallbackGraphNode? FallbackInput { get; }

    internal FallbackGraphNode? FallbackOutput { get; }

    internal bool HasFallback => FallbackInput != null || FallbackOutput != null;

    internal bool CanEdit => _nodeGraph.CanEdit.Value;

    internal void Disconnect()
    {
        if (CanEdit)
            _nodeGraph.EditorContext.GetRequiredService<INodeGraphMutationService>()
                .DisconnectConnection(_nodeGraph.NodeGraph, Connection);
    }

    public ReactiveProperty<InputPortViewModel?> InputPortVM { get; }

    public ReactiveProperty<OutputPortViewModel?> OutputPortVM { get; }

    public ReactivePropertySlim<Point> InputPortPosition { get; } = new();

    public ReactivePropertySlim<Point> OutputPortPosition { get; } = new();

    public IReadOnlyReactiveProperty<ConnectionStatus> Status { get; }

    public void Dispose()
    {
        InputPortVM.Value?.Connections.Remove(this);
        OutputPortVM.Value?.Connections.Remove(this);
        InputPortPosition.Dispose();
        OutputPortPosition.Dispose();
        _disposables.Dispose();
    }
}
