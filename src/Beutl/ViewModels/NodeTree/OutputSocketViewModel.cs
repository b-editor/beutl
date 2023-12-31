using Beutl.NodeTree;

namespace Beutl.ViewModels.NodeTree;

public class OutputSocketViewModel(IOutputSocket? socket, IPropertyEditorContext? propertyEditorContext, Node node)
    : SocketViewModel(socket, propertyEditorContext, node)
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
