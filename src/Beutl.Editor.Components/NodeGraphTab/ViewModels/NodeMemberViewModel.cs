using Beutl.NodeGraph;

using Reactive.Bindings;

namespace Beutl.Editor.Components.NodeGraphTab.ViewModels;

public class NodeMemberViewModel : IDisposable
{
    private bool _disposedValue;

    public NodeMemberViewModel(INodeMember? nodeMember, IPropertyEditorContext? propertyEditorContext, GraphNodeViewModel nodeViewModel)
    {
        if (nodeMember is NodeMember nodeMemberObj)
        {
            Name = nodeMemberObj.GetPropertyChangedObservable(NodeMember.DisplayProperty)
                .Select(e => ((NodeMember)e.Sender).Display?.GetName()
                    ?? ((NodeMember)e.Sender).Name
                    ?? string.Empty)
                .ToReadOnlyReactiveProperty(
                    nodeMemberObj.Display?.GetName() ?? nodeMemberObj.Name ?? string.Empty)!;
        }
        else
        {
            Name = Observable.ReturnThenNever(nodeMember?.Name ?? string.Empty).ToReadOnlyReactiveProperty()!;
        }

        Model = nodeMember;
        PropertyEditorContext = propertyEditorContext;
        GraphNode = nodeViewModel.GraphNode;
        GraphNodeViewModel = nodeViewModel;
    }

    ~NodeMemberViewModel()
    {
        if (!_disposedValue)
        {
            OnDispose();
            _disposedValue = true;
        }
    }

    public ReadOnlyReactiveProperty<string> Name { get; }

    public INodeMember? Model { get; }

    public IPropertyEditorContext? PropertyEditorContext { get; }

    public GraphNode GraphNode { get; }

    public GraphNodeViewModel GraphNodeViewModel { get; }

    public void Dispose()
    {
        if (!_disposedValue)
        {
            OnDispose();
            _disposedValue = true;
            GC.SuppressFinalize(this);
        }
    }

    protected virtual void OnDispose()
    {
        PropertyEditorContext?.Dispose();
        Name.Dispose();
    }
}
