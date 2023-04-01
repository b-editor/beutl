using Beutl.Framework;
using Beutl.NodeTree;

namespace Beutl.ViewModels.NodeTree;

public class OutputSocketViewModel : SocketViewModel
{
    public OutputSocketViewModel(IOutputSocket? socket, IPropertyEditorContext? propertyEditorContext, Node node)
        : base(socket, propertyEditorContext, node)
    {
    }

    public new IOutputSocket? Model => base.Model as IOutputSocket;

    protected override void OnIsConnectedChanged()
    {
        if (Model != null)
        {
            IsConnected.Value = Model.Connections.Count > 0;
        }
    }
}
