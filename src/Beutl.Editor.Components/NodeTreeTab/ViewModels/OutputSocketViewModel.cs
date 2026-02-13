using Beutl.NodeTree;

namespace Beutl.Editor.Components.NodeTreeTab.ViewModels;

public class OutputSocketViewModel(IOutputSocket? socket, IPropertyEditorContext? propertyEditorContext, NodeViewModel nodeViewModel)
    : SocketViewModel(socket, propertyEditorContext, nodeViewModel)
{
    public new IOutputSocket? Model => base.Model as IOutputSocket;
}
