using Beutl.NodeTree;

using Reactive.Bindings;

namespace Beutl.ViewModels.NodeTree;

public class NodeItemViewModel : IDisposable
{
    private bool _disposedValue;

    public NodeItemViewModel(INodeItem? nodeItem, IPropertyEditorContext? propertyEditorContext, Node node)
    {
        if (nodeItem is CoreObject coreObject)
        {
            Name = coreObject.GetPropertyChangedObservable(CoreObject.NameProperty)
                .Select(e => (INodeItem)e.Sender)
                .Publish(nodeItem).RefCount()
                .Select(obj => NodeDisplayNameHelper.GetDisplayName(obj))
                .ToReadOnlyReactiveProperty()!;
        }
        else
        {
            Name = Observable.Return(string.Empty).ToReadOnlyReactiveProperty()!;
        }

        Model = nodeItem;
        PropertyEditorContext = propertyEditorContext;
        Node = node;
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
