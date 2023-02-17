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
    private readonly IDisposable? _disposable;

    public SocketViewModel(ISocket? socket, IPropertyEditorContext? propertyEditorContext, Node node)
        : base(socket, propertyEditorContext, node)
    {
        _disposable = (socket as NodeItem)?.GetObservable(NodeItem.IsValidProperty)?.Subscribe(OnIsConnectedChanged);
        if (socket != null)
        {
            Brush = new(new ImmutableSolidColorBrush(socket.Color.ToAvalonia()));
            socket.Connected += OnSocketConnected;
            socket.Disconnected += OnSocketDisconnected;
        }
        else
        {
            Brush = new(Brushes.Gray);
        }
    }

    public new ISocket? Model => base.Model as ISocket;

    // IsValidがfalseの時、false
    public ReactivePropertySlim<bool> IsConnected { get; } = new();

    public ReactivePropertySlim<IBrush> Brush { get; }

    public ReactivePropertySlim<Point> SocketPosition { get; } = new();

    protected override void OnDispose()
    {
        base.OnDispose();
        _disposable?.Dispose();
        if (Model != null)
        {
            Model.Connected -= OnSocketConnected;
            Model.Disconnected -= OnSocketDisconnected;
        }
    }

    protected virtual void OnSocketDisconnected(object? sender, SocketConnectionChangedEventArgs e)
    {
        if (Model != null)
        {
            OnIsConnectedChanged(((NodeItem)Model).IsValid);
        }
    }

    protected virtual void OnSocketConnected(object? sender, SocketConnectionChangedEventArgs e)
    {
        if (Model != null)
        {
            OnIsConnectedChanged(((NodeItem)Model).IsValid);
        }
    }

    protected virtual void OnIsConnectedChanged(bool? isValid)
    {
    }
}
