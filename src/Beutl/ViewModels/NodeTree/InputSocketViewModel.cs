using Beutl.Framework;
using Beutl.NodeTree;

namespace Beutl.ViewModels.NodeTree;

public class InputSocketViewModel : SocketViewModel
{
    public InputSocketViewModel(IInputSocket socket, IPropertyEditorContext? propertyEditorContext)
        : base(socket, propertyEditorContext)
    {
    }

    public new IInputSocket Model => (IInputSocket)base.Model;

    protected override void OnIsConnectedChanged(bool? isValid)
    {
        if (isValid == false)
        {
            IsConnected.Value = false;
        }
        else
        {
            IsConnected.Value = Model.Connection != null;
        }
    }
}
