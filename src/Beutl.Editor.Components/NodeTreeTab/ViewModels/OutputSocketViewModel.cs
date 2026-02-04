using Beutl.NodeTree;

namespace Beutl.Editor.Components.NodeTreeTab.ViewModels;

public class OutputSocketViewModel(IOutputSocket? socket, IPropertyEditorContext? propertyEditorContext, Node node, IEditorContext editorContext)
    : SocketViewModel(socket, propertyEditorContext, node, editorContext)
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
