using Beutl.NodeGraph;

using Reactive.Bindings;

namespace Beutl.Editor.Components.NodeGraphTab.ViewModels;

public class InputPortViewModel : NodePortViewModel
{
    public InputPortViewModel(IInputPort? port, IPropertyEditorContext? propertyEditorContext, GraphNodeViewModel nodeViewModel)
        : base(port, propertyEditorContext, nodeViewModel)
    {
        GraphNodeViewModel.NodeGraphViewModel.NodeGraph.TopologyChanged += OnTopologyChanged;
        OnTopologyChanged(null, EventArgs.Empty);
    }

    public new IInputPort? Model => base.Model as IInputPort;

    public ReactivePropertySlim<bool> CanConnect { get; } = new(true);

    private void OnTopologyChanged(object? sender, EventArgs e)
        => CanConnect.Value = Model == null || GraphNode.CanConnectInput(Model);

    protected override void OnDispose()
    {
        GraphNodeViewModel.NodeGraphViewModel.NodeGraph.TopologyChanged -= OnTopologyChanged;
        CanConnect.Dispose();
        base.OnDispose();
    }
}
