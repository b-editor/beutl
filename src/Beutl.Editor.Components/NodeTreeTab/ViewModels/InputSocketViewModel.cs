using Beutl.NodeTree;

namespace Beutl.Editor.Components.NodeTreeTab.ViewModels;

public class InputSocketViewModel : SocketViewModel
{
    public InputSocketViewModel(IInputSocket? socket, IPropertyEditorContext? propertyEditorContext, Node node, IEditorContext editorContext)
        : base(socket, propertyEditorContext, node, editorContext)
    {
    }

    public new IInputSocket? Model => base.Model as IInputSocket;

    protected override void OnIsConnectedChanged()
    {
        if (Model != null)
        {
            if (Model is IListInputSocket listSocket)
            {
                IsConnected.Value = listSocket.ListConnections.Count > 0;
            }
            else
            {
                IsConnected.Value = Model.Connection != null;
            }
        }
    }
}
