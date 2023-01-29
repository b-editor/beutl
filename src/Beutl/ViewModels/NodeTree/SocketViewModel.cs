using Avalonia;

using Beutl.Framework;
using Beutl.NodeTree;
using Beutl.Views.NodeTree;

using Reactive.Bindings;

namespace Beutl.ViewModels.NodeTree;

public class SocketViewModel : NodeItemViewModel
{
    public SocketViewModel(ISocket socket, IPropertyEditorContext? propertyEditorContext)
        : base(socket, propertyEditorContext)
    {
        socket.Connected += OnSocketConnected;
        socket.Disconnected += OnSocketDisconnected;
    }

    public new ISocket Model => (ISocket)base.Model;

    public ReactivePropertySlim<SocketState> State { get; } = new();

    public ReactivePropertySlim<Point> SocketPosition { get; } = new();

    protected override void OnDispose()
    {
        base.OnDispose();
        Model.Connected -= OnSocketConnected;
        Model.Disconnected -= OnSocketDisconnected;
    }

    protected virtual void OnSocketDisconnected(object? sender, SocketConnectionChangedEventArgs e)
    {
    }

    protected virtual void OnSocketConnected(object? sender, SocketConnectionChangedEventArgs e)
    {
    }
}
