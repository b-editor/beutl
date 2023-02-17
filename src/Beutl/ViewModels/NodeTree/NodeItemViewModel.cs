using Beutl.Framework;
using Beutl.NodeTree;

namespace Beutl.ViewModels.NodeTree;

public class NodeItemViewModel : IDisposable
{
    private bool _disposedValue;

    public NodeItemViewModel(INodeItem? nodeItem, IPropertyEditorContext? propertyEditorContext, Node node)
    {
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
    }
}
