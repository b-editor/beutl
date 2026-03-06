using System.Collections;
using System.Collections.Specialized;
using Beutl.Editor;
using Beutl.NodeTree;
using Beutl.NodeTree.Nodes;
using Beutl.Services;
using Microsoft.Extensions.DependencyInjection;
using Reactive.Bindings;

namespace Beutl.ViewModels.Editors;

public sealed class NodeTreeModelNodeItemViewModel : IDisposable, IPropertyEditorContextVisitor, IServiceProvider
{
    private readonly CompositeDisposable _disposables = [];
    private readonly string _defaultName;
    private NodeTreeModel? _nodeTree;
    private NodeTreeModelEditorViewModel _parent;

    public NodeTreeModelNodeItemViewModel(LayerInputNode node, int originalIndex, NodeTreeModel nodeTree, NodeTreeModelEditorViewModel parent)
    {
        Node = node;
        OriginalIndex = originalIndex;
        _nodeTree = nodeTree;
        _parent = parent;

        Type nodeType = node.GetType();
        if (NodeRegistry.FindItem(nodeType) is { } regItem)
        {
            _defaultName = regItem.DisplayName;
        }
        else
        {
            _defaultName = nodeType.Name;
        }

        NodeName = node.GetObservable(CoreObject.NameProperty)
            .Select(x => string.IsNullOrWhiteSpace(x) ? _defaultName : x)
            .ToReadOnlyReactiveProperty()
            .DisposeWith(_disposables)!;

        IsExpanded = node.GetObservable(Beutl.NodeTree.Node.IsExpandedProperty)
            .ToReactiveProperty()
            .DisposeWith(_disposables);
        IsExpanded
            .Subscribe(v => Node.IsExpanded = v)
            .DisposeWith(_disposables);

        InitializeProperties();
    }

    public ReadOnlyReactiveProperty<string> NodeName { get; }

    public LayerInputNode Node { get; }

    public int OriginalIndex { get; set; }

    public ReactiveProperty<bool> IsExpanded { get; } = new(true);

    public CoreList<IPropertyEditorContext?> Properties { get; } = [];

    public void Remove()
    {
        if (_nodeTree == null) return;
        // 削除するとDisposeされるので、事前にHistoryManagerを取得しておく
        var historyManager = _parent.GetRequiredService<HistoryManager>();

        Connection[] allConnections = Node.Items
            .SelectMany(i => i switch
            {
                IOutputSocket outputSocket => outputSocket.Connections,
                IListSocket listSocket => listSocket.Connections,
                IInputSocket { Connection: var connection } => [connection],
                _ => []
            })
            .Select(conn => _nodeTree.AllConnections.FirstOrDefault(a => a.Id == conn.Id))
            .Where(a => a != null)
            .ToArray()!;
        foreach (Connection connection in allConnections)
        {
            _nodeTree.Disconnect(connection);
        }
        _nodeTree.Nodes.Remove(Node);
        historyManager.Commit(CommandNames.RemoveNode);
    }

    public void UpdateName(string? name)
    {
        Node.Name = name!;
        _parent.GetRequiredService<HistoryManager>().Commit(CommandNames.RenameNode);
    }

    public void Dispose()
    {
        Node.Items.CollectionChanged -= OnItemsCollectionChanged;
        foreach (IPropertyEditorContext? item in Properties)
        {
            item?.Dispose();
        }

        Properties.Clear();
        _disposables.Dispose();

        _parent = null!;
        _nodeTree = null!;
    }

    private void InitializeProperties()
    {
        var atmp = new IPropertyAdapter[1];
        foreach (INodeItem item in Node.Items)
        {
            Properties.Add(CreatePropertyContext(atmp, item));
        }

        Node.Items.CollectionChanged += OnItemsCollectionChanged;
    }

    private void OnItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        void Add(int index, IList items)
        {
            var atmp = new IPropertyAdapter[1];
            foreach (INodeItem item in items)
            {
                Properties.Insert(index++, CreatePropertyContext(atmp, item));
            }
        }

        void Remove(int index, IList items)
        {
            for (int i = 0; i < items.Count; ++i)
            {
                Properties[index + i]?.Dispose();
            }

            Properties.RemoveRange(index, items.Count);
        }

        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                Add(e.NewStartingIndex, e.NewItems!);
                break;

            case NotifyCollectionChangedAction.Move:
            case NotifyCollectionChangedAction.Replace:
                Remove(e.OldStartingIndex, e.OldItems!);
                Add(e.NewStartingIndex, e.NewItems!);
                break;

            case NotifyCollectionChangedAction.Remove:
                Remove(e.OldStartingIndex, e.OldItems!);
                break;

            case NotifyCollectionChangedAction.Reset:
                foreach (IPropertyEditorContext? item in Properties)
                {
                    item?.Dispose();
                }

                Properties.Clear();
                break;
        }
    }

    private IPropertyEditorContext? CreatePropertyContext(IPropertyAdapter[] atmp, INodeItem item)
    {
        IPropertyEditorContext? context = null;
        if (item is LayerInputNode.ILayerInputSocket socket)
        {
            IPropertyAdapter? aproperty = socket.Property;
            if (aproperty != null)
            {
                atmp[0] = aproperty;
                (_, PropertyEditorExtension? ext) = PropertyEditorService.MatchProperty(atmp);
                ext?.TryCreateContext(atmp, out context);

                context?.Accept(this);
            }
        }

        return context;
    }

    public object? GetService(Type serviceType)
    {
        if (serviceType.IsAssignableTo(typeof(LayerInputNode)))
            return Node;

        return _parent.GetService(serviceType);
    }

    public void Visit(IPropertyEditorContext context)
    {
    }
}
