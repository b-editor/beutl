using Beutl.NodeGraph;

namespace Beutl.Editor.Components.NodeGraphTab.ViewModels;

public class InputPortViewModel : NodePortViewModel
{
    public InputPortViewModel(IInputPort? port, IPropertyEditorContext? propertyEditorContext, GraphNodeViewModel nodeViewModel)
        : base(port, propertyEditorContext, nodeViewModel)
    {
    }

    public new IInputPort? Model => base.Model as IInputPort;
}
