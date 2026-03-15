using Avalonia;
using Avalonia.Media;
using Beutl.NodeGraph;
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
        InputPortVM = connection.GetObservable(Connection.InputProperty)
            .Select(i => i.Value is IInputPort input
                ? _nodeGraph.FindNodePortViewModel(input) as InputPortViewModel
                : null)
            .ToReactiveProperty()
            .DisposeWith(_disposables);
        OutputPortVM = connection.GetObservable(Connection.OutputProperty)
            .Select(i => i.Value is IOutputPort output
                ? _nodeGraph.FindNodePortViewModel(output) as OutputPortViewModel
                : null)
            .ToReactiveProperty()
            .DisposeWith(_disposables);
        InputBrush = InputPortVM.Select(vm => vm?.Color)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);
        OutputBrush = OutputPortVM.Select(vm => vm?.Color)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);
        Status = connection.GetObservable(Connection.StatusProperty)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        InputPortVM.CombineWithPrevious()
            .Subscribe(tuple =>
            {
                if (tuple.OldValue != null)
                {
                    tuple.OldValue.Connections.Remove(this);
                }

                if (tuple.NewValue != null && !tuple.NewValue.Connections.Contains(this))
                {
                    int index = tuple.NewValue.GetInsertionIndex(Connection.Id);
                    tuple.NewValue.Connections.Insert(index, this);
                }
            })
            .DisposeWith(_disposables);

        OutputPortVM.CombineWithPrevious()
            .Subscribe(tuple =>
            {
                if (tuple.OldValue != null)
                {
                    tuple.OldValue.Connections.Remove(this);
                }

                if (tuple.NewValue != null && !tuple.NewValue.Connections.Contains(this))
                {
                    int index = tuple.NewValue.GetInsertionIndex(Connection.Id);
                    tuple.NewValue.Connections.Insert(index, this);
                }
            })
            .DisposeWith(_disposables);
    }

    public Connection Connection { get; }

    public ReactiveProperty<InputPortViewModel?> InputPortVM { get; }

    public ReactiveProperty<OutputPortViewModel?> OutputPortVM { get; }

    public ReactivePropertySlim<Point> InputPortPosition { get; } = new();

    public ReactivePropertySlim<Point> OutputPortPosition { get; } = new();

    public ReadOnlyReactivePropertySlim<IBrush?> InputBrush { get; }

    public ReadOnlyReactivePropertySlim<IBrush?> OutputBrush { get; }

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
