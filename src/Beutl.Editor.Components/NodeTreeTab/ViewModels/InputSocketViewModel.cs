using Beutl.NodeTree;

namespace Beutl.Editor.Components.NodeTreeTab.ViewModels;

public class InputSocketViewModel : SocketViewModel
{
    public InputSocketViewModel(IInputSocket? socket, IPropertyEditorContext? propertyEditorContext, NodeViewModel nodeViewModel)
        : base(socket, propertyEditorContext, nodeViewModel)
    {
    }

    public new IInputSocket? Model => base.Model as IInputSocket;
}
