using System.Collections;
using System.Collections.Specialized;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Beutl.Controls;
using Beutl.Editor.Services;
using Beutl.NodeTree;
using Beutl.NodeTree.Nodes.Group;
using FluentAvalonia.UI.Media;
using Microsoft.Extensions.DependencyInjection;
using Reactive.Bindings;

namespace Beutl.Editor.Components.NodeTreeTab.ViewModels;

public sealed class NodeViewModel : IDisposable, IJsonSerializable, IPropertyEditorContextVisitor, IServiceProvider
{
    private readonly CompositeDisposable _disposables = [];
    private readonly string _defaultName;

    public NodeViewModel(Node node, NodeTreeViewModel nodeTreeViewModel)
    {
        Node = node;
        EditorContext = nodeTreeViewModel.EditorContext;
        NodeTreeViewModel = nodeTreeViewModel;
        Type nodeType = node.GetType();
        if (NodeRegistry.FindItem(nodeType) is { } regItem)
        {
            _defaultName = regItem.DisplayName;

            var color = new Color2(regItem.AccentColor.ToAvaColor());
            Color = new ImmutableLinearGradientBrush(
                [
                    new ImmutableGradientStop(0, color.WithAlphaf(0.1f)),
                    new ImmutableGradientStop(1, color.WithAlphaf(0.01f))
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

        IsExpanded = node.GetObservable(Node.IsExpandedProperty)
            .ToReactiveProperty()
            .DisposeWith(_disposables);

        IsExpanded.Subscribe(v => Node.IsExpanded = v)
            .DisposeWith(_disposables);

        Position = node.GetObservable(Node.PositionProperty)
            .Select(x => new Point(x.X, x.Y))
            .ToReactiveProperty()
            .DisposeWith(_disposables);

        Delete.Subscribe(() =>
        {
            NodeTreeModel? tree = Node.FindHierarchicalParent<NodeTreeModel>();
            if (tree != null)
            {
                tree.Nodes.Remove(Node);
                EditorContext.GetRequiredService<HistoryManager>().Commit(CommandNames.RemoveNode);
            }
        });

        InitItems();
    }

    public Node Node { get; }

    public IEditorContext EditorContext { get; }

    public NodeTreeViewModel NodeTreeViewModel { get; }

    public ReadOnlyReactiveProperty<string> NodeName { get; }

    public IBrush Color { get; }

    public ReactiveProperty<bool> IsSelected { get; } = new();

    public bool IsGroupNode => Node is GroupNode;

    public ReactiveProperty<Point> Position { get; }

    public ReactiveProperty<bool> IsExpanded { get; }

    public ReactiveCommand Delete { get; } = new();

    public CoreList<NodeItemViewModel> Items { get; } = [];

    public void Dispose()
    {
        Node.Items.CollectionChanged -= OnItemsCollectionChanged;
        foreach (NodeItemViewModel item in Items)
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
        foreach (INodeItem item in Node.Items)
        {
            Items.Add(CreateNodeItemViewModel(atmp, item));
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
                Items.Insert(index++, CreateNodeItemViewModel(atmp, item));
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
                foreach (NodeItemViewModel item in Items)
                {
                    item.Dispose();
                }

                Items.Clear();
                break;
        }
    }

    private NodeItemViewModel CreateNodeItemViewModel(IPropertyAdapter[] atmp, INodeItem item)
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
        return CreateNodeItemViewModelCore(item, context);
    }

    private NodeItemViewModel CreateNodeItemViewModelCore(INodeItem nodeItem,
        IPropertyEditorContext? propertyEditorContext)
    {
        return nodeItem switch
        {
            INodeMonitor monitor => new NodeMonitorViewModel(monitor, propertyEditorContext, this),
            IOutputSocket osocket => new OutputSocketViewModel(osocket, propertyEditorContext, this),
            IInputSocket isocket => new InputSocketViewModel(isocket, propertyEditorContext, this),
            ISocket socket => new SocketViewModel(socket, propertyEditorContext, this),
            _ => new NodeItemViewModel(nodeItem, propertyEditorContext, this),
        };
    }

    public void UpdatePosition(IEnumerable<NodeViewModel> selection)
    {
        foreach (var item in selection)
        {
            item.Node.Position = (item.Position.Value.X, item.Position.Value.Y);
        }

        Node.Position = (Position.Value.X, Position.Value.Y);

        EditorContext.GetRequiredService<HistoryManager>().Commit(CommandNames.MoveNode);
    }

    public void UpdateName(string? name)
    {
        Node.Name = name!;
        EditorContext.GetRequiredService<HistoryManager>().Commit(CommandNames.RenameNode);
    }

    public void WriteToJson(JsonObject json)
    {
        json[nameof(IsExpanded)] = IsExpanded.Value;

        var itemsJson = new JsonObject();
        foreach (NodeItemViewModel item in Items)
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
        foreach (NodeItemViewModel item in Items)
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
        if (serviceType == typeof(Node))
        {
            return Node;
        }

        return EditorContext.GetService(serviceType);
    }
}
