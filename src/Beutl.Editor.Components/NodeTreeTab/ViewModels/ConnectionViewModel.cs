using Avalonia;
using Avalonia.Media;

using Beutl.NodeTree;

using Reactive.Bindings;

namespace Beutl.Editor.Components.NodeTreeTab.ViewModels;

public class ConnectionViewModel : IDisposable
{
    public ConnectionViewModel(Connection connection, IBrush inputBrush, IBrush outputBrush)
    {
        Connection = connection;
        InputBrush = new(inputBrush);
        OutputBrush = new(outputBrush);
        Status = connection.GetObservable(Connection.StatusProperty)
            .ToReadOnlyReactivePropertySlim();
    }

    public Connection Connection { get; }

    public ReactivePropertySlim<Point> InputSocketPosition { get; } = new();

    public ReactivePropertySlim<Point> OutputSocketPosition { get; } = new();

    public ReactivePropertySlim<IBrush> InputBrush { get; }

    public ReactivePropertySlim<IBrush> OutputBrush { get; }

    public IReadOnlyReactiveProperty<ConnectionStatus> Status { get; }

    public void Dispose()
    {
        InputSocketPosition.Dispose();
        OutputSocketPosition.Dispose();
        InputBrush.Dispose();
        OutputBrush.Dispose();
        Status.Dispose();
    }
}
