using Beutl.NodeTree;

using Reactive.Bindings;

namespace Beutl.Editor.Components.NodeTreeTab.ViewModels;

public class NodeItemViewModel : IDisposable
{
    private bool _disposedValue;

    public NodeItemViewModel(INodeItem? nodeItem, IPropertyEditorContext? propertyEditorContext, NodeViewModel nodeViewModel)
    {
        if (nodeItem is NodeItem nodeItemObj)
        {
            Name = nodeItemObj.GetPropertyChangedObservable(NodeItem.DisplayProperty)
                .Select(e => ((NodeItem)e.Sender).Display?.GetName()
                    ?? ((NodeItem)e.Sender).Name
                    ?? string.Empty)
                .ToReadOnlyReactiveProperty(
                    nodeItemObj.Display?.GetName() ?? nodeItemObj.Name ?? string.Empty)!;
        }
        else
        {
            Name = Observable.ReturnThenNever(nodeItem?.Name ?? string.Empty).ToReadOnlyReactiveProperty()!;
        }

        Model = nodeItem;
        PropertyEditorContext = propertyEditorContext;
        Node = nodeViewModel.Node;
        NodeViewModel = nodeViewModel;
    }

    ~NodeItemViewModel()
    {
        if (!_disposedValue)
        {
            OnDispose();
            _disposedValue = true;
        }
    }

    public ReadOnlyReactiveProperty<string> Name { get; }

    public INodeItem? Model { get; }

    public IPropertyEditorContext? PropertyEditorContext { get; }

    public Node Node { get; }

    public NodeViewModel NodeViewModel { get; }

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
