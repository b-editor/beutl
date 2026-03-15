using Beutl.Editor.Components.NodeGraphTab.ViewModels;

namespace Beutl.Editor.Components.NodeGraphTab.Views;

public class NodePortConnectRequestedEventArgs(NodePortViewModel target, bool isConnected) : EventArgs
{
    public NodePortViewModel Target { get; } = target;

    public ConnectionViewModel? Connection { get; set; }

    public bool IsConnected { get; set; } = isConnected;
}
