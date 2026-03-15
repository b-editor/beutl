using System.Collections;
using System.Collections.Specialized;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Beutl.Controls;
using Beutl.Editor.Services;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Nodes.Group;
using FluentAvalonia.UI.Media;
using Microsoft.Extensions.DependencyInjection;
using Reactive.Bindings;

namespace Beutl.Editor.Components.NodeGraphTab.ViewModels;

public sealed class GraphNodeViewModel : IDisposable, IJsonSerializable, IPropertyEditorContextVisitor, IServiceProvider
{
    private readonly CompositeDisposable _disposables = [];
    private readonly string _defaultName;

    public GraphNodeViewModel(GraphNode node, NodeGraphViewModel nodeGraphViewModel)
    {
        GraphNode = node;
        EditorContext = nodeGraphViewModel.EditorContext;
        NodeGraphViewModel = nodeGraphViewModel;
        Type nodeType = node.GetType();
        if (GraphNodeRegistry.FindItem(nodeType) is { } regItem)
        {
            _defaultName = regItem.DisplayName;

            var color = new Color2(regItem.AccentColor.ToAvaColor());
            Color = new ImmutableLinearGradientBrush(
                [
                    new ImmutableGradientStop(0, color.WithAlphaf(0.9f)),
                    new ImmutableGradientStop(1, color.WithAlphaf(0.8f))
                ],
                startPoint: RelativePoint.TopLeft,
                endPoint: RelativePoint.BottomRight);
        }
        else
        {
            _defaultName = nodeType.Name;
            Color = Brushes.Transparent;
        }

        NodeName = node.GetObservable(CoreObject.NameProperty)
            .Select(x => string.IsNullOrWhiteSpace(x) ? _defaultName : x)
            .ToReadOnlyReactiveProperty()
            .DisposeWith(_disposables)!;

        IsExpanded = node.GetObservable(GraphNode.IsExpandedProperty)
            .ToReactiveProperty()
            .DisposeWith(_disposables);

        IsExpanded.Subscribe(v => GraphNode.IsExpanded = v)
            .DisposeWith(_disposables);

        Position = node.GetObservable(GraphNode.PositionProperty)
            .Select(x => new Point(x.X, x.Y))
            .ToReactiveProperty()
            .DisposeWith(_disposables);

        Delete.Subscribe(() =>
        {
            GraphModel? tree = GraphNode.FindHierarchicalParent<GraphModel>();
            if (tree != null)
            {
                Connection[] allConnections = GraphNode.Items
                    .SelectMany(i => i switch
                    {
                        IOutputPort outputNodePort => outputNodePort.Connections,
                        IListPort listNodePort => listNodePort.Connections,
                        IInputPort { Connection: var connection } => [connection],
                        _ => []
                    })
                    .Select(conn => tree.AllConnections.FirstOrDefault(a => a.Id == conn.Id))
                    .Where(a => a != null)
                    .ToArray()!;
                foreach (Connection connection in allConnections)
                {
                    tree.Disconnect(connection);
                }
                tree.Nodes.Remove(GraphNode);
                EditorContext.GetRequiredService<HistoryManager>().Commit(CommandNames.RemoveNode);
            }
        });

        InitItems();
    }

    public GraphNode GraphNode { get; }

    public IEditorContext EditorContext { get; }

    public NodeGraphViewModel NodeGraphViewModel { get; }

    public ReadOnlyReactiveProperty<string> NodeName { get; }

    public IBrush Color { get; }

    public ReactiveProperty<bool> IsSelected { get; } = new();

    public bool IsGroupNode => GraphNode is GroupNode;

    public ReactiveProperty<Point> Position { get; }

    public ReactiveProperty<bool> IsExpanded { get; }

    public ReactiveCommand Delete { get; } = new();

    public CoreList<NodeMemberViewModel> Items { get; } = [];

    public void Dispose()
    {
        GraphNode.Items.CollectionChanged -= OnItemsCollectionChanged;
        foreach (NodeMemberViewModel item in Items)
        {
            item.Dispose();
        }

        Items.Clear();
        Position.Dispose();
        _disposables.Dispose();
    }

    private void InitItems()
    {
        var atmp = new IPropertyAdapter[1];
        foreach (INodeMember item in GraphNode.Items)
        {
            Items.Add(CreateNodeMemberViewModel(atmp, item));
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
                Items.Insert(index++, CreateNodeMemberViewModel(atmp, item));
            }
        }

        void Remove(int index, IList items)
        {
            for (int i = 0; i < items.Count; ++i)
            {
                Items[index + i].Dispose();
            }

            Items.RemoveRange(index, items.Count);
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
                foreach (NodeMemberViewModel item in Items)
                {
                    item.Dispose();
                }

                Items.Clear();
                break;
        }
    }

    private NodeMemberViewModel CreateNodeMemberViewModel(IPropertyAdapter[] atmp, INodeMember item)
    {
        IPropertyEditorFactory factory = EditorContext.GetRequiredService<IPropertyEditorFactory>();
        IPropertyEditorContext? context = null;
        if (item.Property is { } aproperty)
        {
            atmp[0] = aproperty;
            (_, PropertyEditorExtension? ext) = factory.MatchProperty(atmp);
            ext?.TryCreateContextForNode(atmp, out context);
        }

        context?.Accept(this);
        return CreateNodeMemberViewModelCore(item, context);
    }

    private NodeMemberViewModel CreateNodeMemberViewModelCore(INodeMember nodeMember,
        IPropertyEditorContext? propertyEditorContext)
    {
        return nodeMember switch
        {
            INodeMonitor monitor => new NodeMonitorViewModel(monitor, propertyEditorContext, this),
            IOutputPort outputPort => new OutputPortViewModel(outputPort, propertyEditorContext, this),
            IInputPort inputPort => new InputPortViewModel(inputPort, propertyEditorContext, this),
            INodePort nodePort => new NodePortViewModel(nodePort, propertyEditorContext, this),
            _ => new NodeMemberViewModel(nodeMember, propertyEditorContext, this),
        };
    }

    public void UpdatePosition(IEnumerable<GraphNodeViewModel> selection)
    {
        foreach (var item in selection)
        {
            item.GraphNode.Position = (item.Position.Value.X, item.Position.Value.Y);
        }

        GraphNode.Position = (Position.Value.X, Position.Value.Y);

        EditorContext.GetRequiredService<HistoryManager>().Commit(CommandNames.MoveNode);
    }

    public void UpdateName(string? name)
    {
        GraphNode.Name = name!;
        EditorContext.GetRequiredService<HistoryManager>().Commit(CommandNames.RenameNode);
    }

    public void WriteToJson(JsonObject json)
    {
        json[nameof(IsExpanded)] = IsExpanded.Value;

        var itemsJson = new JsonObject();
        foreach (NodeMemberViewModel item in Items)
        {
            if (item.Model != null
                && item.PropertyEditorContext != null)
            {
                var itemJson = new JsonObject();
                item.PropertyEditorContext.WriteToJson(itemJson);

                itemsJson[item.Model.Id.ToString()] = itemJson;
            }
        }

        json[nameof(Items)] = itemsJson;
    }

    public void ReadFromJson(JsonObject json)
    {
        IsExpanded.Value = (bool)json[nameof(IsExpanded)]!;

        JsonObject itemsJson = json[nameof(Items)]!.AsObject();
        foreach (NodeMemberViewModel item in Items)
        {
            if (item.Model != null
                && item.PropertyEditorContext != null
                && itemsJson.TryGetPropertyValue(item.Model.Id.ToString(), out JsonNode? itemJson))
            {
                item.PropertyEditorContext.ReadFromJson(itemJson!.AsObject());
            }
        }
    }

    public void Visit(IPropertyEditorContext context)
    {
    }

    public object? GetService(Type serviceType)
    {
        if (serviceType == typeof(GraphNode))
        {
            return GraphNode;
        }

        return EditorContext.GetService(serviceType);
    }
}
