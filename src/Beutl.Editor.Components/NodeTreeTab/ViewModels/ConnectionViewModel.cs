using Avalonia;
using Avalonia.Media;
using Beutl.NodeTree;
using Reactive.Bindings;

namespace Beutl.Editor.Components.NodeTreeTab.ViewModels;

public class ConnectionViewModel : IDisposable
{
    private readonly CompositeDisposable _disposables = new();
    private readonly NodeTreeViewModel _nodeTree;

    public ConnectionViewModel(NodeTreeViewModel nodeTree, Connection connection)
    {
        _nodeTree = nodeTree;
        Connection = connection;
        InputSocketVM = connection.GetObservable(Connection.InputProperty)
            .Select(i => i.Value is IInputSocket input
                ? _nodeTree.FindSocketViewModel(input) as InputSocketViewModel
                : null)
            .ToReactiveProperty()
            .DisposeWith(_disposables);
        OutputSocketVM = connection.GetObservable(Connection.OutputProperty)
            .Select(i => i.Value is IOutputSocket output
                ? _nodeTree.FindSocketViewModel(output) as OutputSocketViewModel
                : null)
            .ToReactiveProperty()
            .DisposeWith(_disposables);
        InputBrush = InputSocketVM.Select(vm => vm?.Color)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);
        OutputBrush = OutputSocketVM.Select(vm => vm?.Color)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);
        Status = connection.GetObservable(Connection.StatusProperty)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        InputSocketVM.CombineWithPrevious()
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

        OutputSocketVM.CombineWithPrevious()
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

    public ReactiveProperty<InputSocketViewModel?> InputSocketVM { get; }

    public ReactiveProperty<OutputSocketViewModel?> OutputSocketVM { get; }

    public ReactivePropertySlim<Point> InputSocketPosition { get; } = new();

    public ReactivePropertySlim<Point> OutputSocketPosition { get; } = new();

    public ReadOnlyReactivePropertySlim<IBrush?> InputBrush { get; }

    public ReadOnlyReactivePropertySlim<IBrush?> OutputBrush { get; }

    public IReadOnlyReactiveProperty<ConnectionStatus> Status { get; }

    public void Dispose()
    {
        InputSocketVM.Value?.Connections.Remove(this);
        OutputSocketVM.Value?.Connections.Remove(this);
        InputSocketPosition.Dispose();
        OutputSocketPosition.Dispose();
        _disposables.Dispose();
    }
}
