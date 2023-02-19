using System.Collections;
using System.Collections.Specialized;

using Beutl.Framework;
using Beutl.NodeTree;
using Beutl.NodeTree.Nodes;
using Beutl.NodeTree.Nodes.Group;
using Beutl.Services;

using Reactive.Bindings;

namespace Beutl.ViewModels.NodeTree;

public sealed class NodeInputViewModel : IDisposable
{
    private readonly CompositeDisposable _disposables = new();
    private readonly NodeTreeSpace _nodeTree;

    public NodeInputViewModel(LayerInputNode node, int originalIndex, NodeTreeSpace nodeTree)
    {
        Node = node;
        OriginalIndex = originalIndex;
        _nodeTree = nodeTree;

        Type nodeType = node.GetType();
        if (NodeRegistry.FindItem(nodeType) is { } regItem)
        {
            NodeName = regItem.DisplayName;
        }
        else
        {
            NodeName = nodeType.Name;
        }

        IsExpanded = node.GetObservable(Beutl.NodeTree.Node.IsExpandedProperty)
            .ToReactiveProperty()
            .DisposeWith(_disposables);
        IsExpanded//.ObserveOnRendererThread()
            .Subscribe(v => Node.IsExpanded = v)
            .DisposeWith(_disposables);

        InitializeProperties();
    }

    public string NodeName { get; }

    public LayerInputNode Node { get; }

    public int OriginalIndex { get; }

    public ReactiveProperty<bool> IsExpanded { get; } = new(true);

    public CoreList<IPropertyEditorContext?> Properties { get; } = new();

    public void Remove()
    {
        _nodeTree.Nodes.BeginRecord<Node>()
            .Remove(Node)
            .ToCommand()
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
    }

    private void InitializeProperties()
    {
        var ctmp = new CoreProperty[1];
        var atmp = new IAbstractProperty[1];
        foreach (INodeItem item in Node.Items)
        {
            Properties.Add(CreatePropertyContext(ctmp, atmp, item));
        }

        Node.Items.CollectionChanged += OnItemsCollectionChanged;
    }

    private void OnItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        void Add(int index, IList items)
        {
            var ctmp = new CoreProperty[1];
            var atmp = new IAbstractProperty[1];
            foreach (INodeItem item in items)
            {
                Properties.Insert(index++, CreatePropertyContext(ctmp, atmp, item));
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

    private static IPropertyEditorContext? CreatePropertyContext(CoreProperty[] ctmp, IAbstractProperty[] atmp, INodeItem item)
    {
        IPropertyEditorContext? context = null;
        if (item is LayerInputNode.ILayerInputSocket socket)
        {
            IAbstractProperty? aproperty = socket.GetProperty();
            if (aproperty != null)
            {
                ctmp[0] = aproperty.Property;
                atmp[0] = aproperty;
                (_, PropertyEditorExtension ext) = PropertyEditorService.MatchProperty(ctmp);
                ext?.TryCreateContext(atmp, out context);
            }
        }

        return context;
    }
}
