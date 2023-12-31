using System.Collections;
using System.Collections.Specialized;

using Beutl.Commands;
using Beutl.NodeTree;
using Beutl.NodeTree.Nodes;
using Beutl.Services;

using Reactive.Bindings;

namespace Beutl.ViewModels.NodeTree;

public sealed class NodeInputViewModel : IDisposable, IPropertyEditorContextVisitor, IServiceProvider
{
    private readonly CompositeDisposable _disposables = [];
    private readonly string _defaultName;
    private NodeTreeModel _nodeTree;
    private NodeTreeInputViewModel _parent;

    public NodeInputViewModel(LayerInputNode node, int originalIndex, NodeTreeInputViewModel parent)
    {
        Node = node;
        OriginalIndex = originalIndex;
        _parent = parent;
        _nodeTree = parent.Model.NodeTree;

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
        _nodeTree.Nodes.BeginRecord<Node>()
            .Remove(Node)
            .ToCommand()
            .DoAndRecord(CommandRecorder.Default);
    }

    public void UpdateName(string? name)
    {
        new ChangePropertyCommand<string>(Node, CoreObject.NameProperty, name, Node.Name)
            .DoAndRecord(CommandRecorder.Default);
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
        var atmp = new IAbstractProperty[1];
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
            var atmp = new IAbstractProperty[1];
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

    private IPropertyEditorContext? CreatePropertyContext(IAbstractProperty[] atmp, INodeItem item)
    {
        IPropertyEditorContext? context = null;
        if (item is LayerInputNode.ILayerInputSocket socket)
        {
            IAbstractProperty? aproperty = socket.GetProperty();
            if (aproperty != null)
            {
                atmp[0] = aproperty;
                (_, PropertyEditorExtension ext) = PropertyEditorService.MatchProperty(atmp);
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
