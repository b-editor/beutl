using System.Collections;
using System.Collections.Specialized;
using Beutl.Editor;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Nodes;
using Beutl.Services;
using Microsoft.Extensions.DependencyInjection;
using Reactive.Bindings;

namespace Beutl.ViewModels.Editors;

public sealed class GraphModelNodeMemberViewModel : IDisposable, IPropertyEditorContextVisitor, IServiceProvider
{
    private readonly CompositeDisposable _disposables = [];
    private readonly string _defaultName;
    private GraphModel? _graphModel;
    private GraphModelEditorViewModel _parent;

    public GraphModelNodeMemberViewModel(LayerInputNode node, int originalIndex, GraphModel graphModel, GraphModelEditorViewModel parent)
    {
        GraphNode = node;
        OriginalIndex = originalIndex;
        _graphModel = graphModel;
        _parent = parent;

        Type nodeType = node.GetType();
        if (GraphNodeRegistry.FindItem(nodeType) is { } regItem)
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

        IsExpanded = node.GetObservable(Beutl.NodeGraph.GraphNode.IsExpandedProperty)
            .ToReactiveProperty()
            .DisposeWith(_disposables);
        IsExpanded
            .Subscribe(v => GraphNode.IsExpanded = v)
            .DisposeWith(_disposables);

        InitializeProperties();
    }

    public ReadOnlyReactiveProperty<string> NodeName { get; }

    public LayerInputNode GraphNode { get; }

    public int OriginalIndex { get; set; }

    public ReactiveProperty<bool> IsExpanded { get; } = new(true);

    public CoreList<IPropertyEditorContext?> Properties { get; } = [];

    public void Remove()
    {
        if (_graphModel == null) return;
        // 削除するとDisposeされるので、事前にHistoryManagerを取得しておく
        var historyManager = _parent.GetRequiredService<HistoryManager>();

        Connection[] allConnections = GraphNode.Items
            .SelectMany(i => i switch
            {
                IOutputPort outputNodePort => outputNodePort.Connections,
                IListPort listNodePort => listNodePort.Connections,
                IInputPort { Connection: var connection } => [connection],
                _ => []
            })
            .Select(conn => _graphModel.AllConnections.FirstOrDefault(a => a.Id == conn.Id))
            .Where(a => a != null)
            .ToArray()!;
        foreach (Connection connection in allConnections)
        {
            _graphModel.Disconnect(connection);
        }
        _graphModel.Nodes.Remove(GraphNode);
        historyManager.Commit(CommandNames.RemoveNode);
    }

    public void UpdateName(string? name)
    {
        GraphNode.Name = name!;
        _parent.GetRequiredService<HistoryManager>().Commit(CommandNames.RenameNode);
    }

    public void Dispose()
    {
        GraphNode.Items.CollectionChanged -= OnItemsCollectionChanged;
        foreach (IPropertyEditorContext? item in Properties)
        {
            item?.Dispose();
        }

        Properties.Clear();
        _disposables.Dispose();

        _parent = null!;
        _graphModel = null!;
    }

    private void InitializeProperties()
    {
        var atmp = new IPropertyAdapter[1];
        foreach (INodeMember item in GraphNode.Items)
        {
            Properties.Add(CreatePropertyContext(atmp, item));
        }

        GraphNode.Items.CollectionChanged += OnItemsCollectionChanged;
    }

    private void OnItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        void Add(int index, IList items)
        {
            var atmp = new IPropertyAdapter[1];
            foreach (INodeMember item in items)
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

    private IPropertyEditorContext? CreatePropertyContext(IPropertyAdapter[] atmp, INodeMember item)
    {
        IPropertyEditorContext? context = null;
        if (item is LayerInputNode.ILayerInputPort port)
        {
            IPropertyAdapter? aproperty = port.Property;
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
            return GraphNode;

        return _parent.GetService(serviceType);
    }

    public void Visit(IPropertyEditorContext context)
    {
    }
}
