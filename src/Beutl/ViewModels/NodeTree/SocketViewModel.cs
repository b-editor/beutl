using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;

using Beutl.Framework;
using Beutl.NodeTree;
using Beutl.Views.NodeTree;

using Reactive.Bindings;

namespace Beutl.ViewModels.NodeTree;

public class SocketViewModel : NodeItemViewModel
{
    private readonly IDisposable _disposable;

    public SocketViewModel(ISocket socket, IPropertyEditorContext? propertyEditorContext)
        : base(socket, propertyEditorContext)
    {
        Brush = new(new ImmutableSolidColorBrush(socket.Color.ToAvalonia()));
        _disposable = ((NodeItem)socket).GetObservable(NodeItem.IsValidProperty).Subscribe(OnIsConnectedChanged);
        socket.Connected += OnSocketConnected;
        socket.Disconnected += OnSocketDisconnected;
    }

    public new ISocket Model => (ISocket)base.Model;

    // IsValidがfalseの時、false
    public ReactivePropertySlim<bool> IsConnected { get; } = new();

    public ReactivePropertySlim<IBrush> Brush { get; }

    public ReactivePropertySlim<Point> SocketPosition { get; } = new();

    protected override void OnDispose()
    {
        base.OnDispose();
        _disposable.Dispose();
        Model.Connected -= OnSocketConnected;
        Model.Disconnected -= OnSocketDisconnected;
    }

    protected virtual void OnSocketDisconnected(object? sender, SocketConnectionChangedEventArgs e)
    {
        OnIsConnectedChanged(((NodeItem)Model).IsValid);
    }

    protected virtual void OnSocketConnected(object? sender, SocketConnectionChangedEventArgs e)
    {
        OnIsConnectedChanged(((NodeItem)Model).IsValid);
    }

    protected virtual void OnIsConnectedChanged(bool? isValid)
    {
    }
}
