using Beutl.NodeTree;

namespace Beutl.ViewModels.NodeTree;

public class OutputSocketViewModel(IOutputSocket? socket, IPropertyEditorContext? propertyEditorContext, Node node, EditViewModel editViewModel)
    : SocketViewModel(socket, propertyEditorContext, node, editViewModel)
{
    public new IOutputSocket? Model => base.Model as IOutputSocket;

    protected override void OnIsConnectedChanged()
    {
        if (Model != null)
        {
            IsConnected.Value = Model.Connections.Count > 0;
        }
    }
}
