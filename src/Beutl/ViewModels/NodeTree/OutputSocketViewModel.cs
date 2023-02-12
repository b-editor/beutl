using Beutl.Framework;
using Beutl.NodeTree;

namespace Beutl.ViewModels.NodeTree;

public class OutputSocketViewModel : SocketViewModel
{
    public OutputSocketViewModel(IOutputSocket socket, IPropertyEditorContext? propertyEditorContext)
        : base(socket, propertyEditorContext)
    {
    }

    public new IOutputSocket Model => (IOutputSocket)base.Model;

    protected override void OnIsConnectedChanged(bool? isValid)
    {
        IsConnected.Value = Model.Connections.Count > 0;
    }
}
