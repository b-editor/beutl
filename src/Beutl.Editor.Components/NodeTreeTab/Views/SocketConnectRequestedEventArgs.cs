using Beutl.Editor.Components.NodeTreeTab.ViewModels;

namespace Beutl.Editor.Components.NodeTreeTab.Views;

public class SocketConnectRequestedEventArgs(SocketViewModel target, bool isConnected) : EventArgs
{
    public SocketViewModel Target { get; } = target;

    public ConnectionViewModel? Connection { get; set; }

    public bool IsConnected { get; set; } = isConnected;
}
