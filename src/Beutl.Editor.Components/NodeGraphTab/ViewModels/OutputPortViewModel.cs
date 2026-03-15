using Beutl.NodeGraph;

namespace Beutl.Editor.Components.NodeGraphTab.ViewModels;

public class OutputPortViewModel(IOutputPort? port, IPropertyEditorContext? propertyEditorContext, GraphNodeViewModel nodeViewModel)
    : NodePortViewModel(port, propertyEditorContext, nodeViewModel)
{
    public new IOutputPort? Model => base.Model as IOutputPort;
}
