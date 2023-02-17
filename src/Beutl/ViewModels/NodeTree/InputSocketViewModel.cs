using Beutl.Framework;
using Beutl.NodeTree;

namespace Beutl.ViewModels.NodeTree;

public class InputSocketViewModel : SocketViewModel
{
    public InputSocketViewModel(IInputSocket? socket, IPropertyEditorContext? propertyEditorContext, Node node)
        : base(socket, propertyEditorContext, node)
    {
    }

    public new IInputSocket? Model => base.Model as IInputSocket;

    protected override void OnIsConnectedChanged(bool? isValid)
    {
        if (isValid == false)
        {
            IsConnected.Value = false;
        }
        else if (Model != null)
        {
            IsConnected.Value = Model.Connection != null;
        }
    }
}
