using System.Text.Json.Nodes;

using Avalonia.Collections.Pooled;

using Beutl.Framework;
using Beutl.NodeTree;
using Beutl.ProjectSystem;
using Beutl.Services.PrimitiveImpls;

using Reactive.Bindings;

namespace Beutl.ViewModels.NodeTree;

public sealed class NodeTreeNavigationItem : IDisposable
{
    internal Lazy<NodeTreeViewModel> _lazyViewModel;

    public NodeTreeNavigationItem(NodeTreeViewModel viewModel, ReadOnlyReactivePropertySlim<string> name, NodeTreeModel nodeTree)
    {
        _lazyViewModel = new Lazy<NodeTreeViewModel>(viewModel);
        Name = name;
        NodeTree = nodeTree;
    }

    public NodeTreeNavigationItem(ReadOnlyReactivePropertySlim<string> name, NodeTreeModel nodeTree)
    {
        NodeTree = nodeTree;
        _lazyViewModel = new Lazy<NodeTreeViewModel>(() => new NodeTreeViewModel(NodeTree));
        Name = name;
    }

    public NodeTreeViewModel ViewModel => _lazyViewModel.Value;

    public ReadOnlyReactivePropertySlim<string> Name { get; }

    public NodeTreeModel NodeTree { get; private set; }

    public void Dispose()
    {
        if (_lazyViewModel.IsValueCreated)
        {
            _lazyViewModel.Value.Dispose();
        }

        Name.Dispose();
        _lazyViewModel = null!;
        NodeTree = null!;
    }
}

public sealed class NodeTreeTabViewModel : IToolContext
{
    private readonly ReactiveProperty<bool> _isSelected = new(true);
    private readonly CompositeDisposable _disposables = new();
    private EditViewModel _editViewModel;

    public NodeTreeTabViewModel(EditViewModel editViewModel)
    {
        _editViewModel = editViewModel;

        Layer.Subscribe(v =>
        {
            foreach (NodeTreeNavigationItem item in Items)
            {
                item.Dispose();
            }
            Items.Clear();

            NodeTree.Value?.Dispose();
            NodeTree.Value = null;

            if (v != null)
            {
                NodeTree.Value = new NodeTreeViewModel(v.NodeTree);
                IObservable<string> name = v.GetObservable(CoreObject.NameProperty);
                IObservable<string> fileName = v.GetObservable(ProjectItem.FileNameProperty)
                    .Select(x => Path.GetFileNameWithoutExtension(x));

                Items.Add(new NodeTreeNavigationItem(
                    viewModel: NodeTree.Value,
                    nodeTree: v.NodeTree,
                    name: name.CombineLatest(fileName)
                        .Select(x => string.IsNullOrWhiteSpace(x.First) ? x.Second : x.First)
                        .ToReadOnlyReactivePropertySlim()!));
            }
        }).DisposeWith(_disposables);
    }

    public string Header => Strings.NodeTree;

    public ToolTabExtension Extension => NodeTreeTabExtension.Instance;

    public IReactiveProperty<bool> IsSelected => _isSelected;

    public ToolTabExtension.TabPlacement Placement => ToolTabExtension.TabPlacement.Bottom;

    public ReactivePropertySlim<Element?> Layer { get; } = new();

    public ReactivePropertySlim<NodeTreeViewModel?> NodeTree { get; } = new();

    public CoreList<NodeTreeNavigationItem> Items { get; } = new();

    public void Dispose()
    {
        _disposables.Dispose();
        foreach (NodeTreeNavigationItem item in Items)
        {
            item.Dispose();
        }

        Items.Clear();
        NodeTree.Dispose();
        NodeTree.Value?.Dispose();
        NodeTree.Value = null;
        Layer.Value = null;
        _editViewModel = null!;
    }

    public void NavigateTo(int index)
    {
        if (index < Items.Count)
        {
            for (int i = index + 1; i < Items.Count; i++)
            {
                Items[i].Dispose();
            }

            if (index + 1 < Items.Count)
            {
                Items.RemoveRange(index + 1, Items.Count - (index + 1));
            }

            NodeTree.Value = Items[index].ViewModel;
        }
    }

    public NodeTreeNavigationItem? FindItem(NodeTreeModel nodeTree)
    {
        foreach (NodeTreeNavigationItem navItem in Items)
        {
            if (navItem.NodeTree == nodeTree)
            {
                return navItem;
            }
        }

        return null;
    }

    public void NavigateTo(NodeTreeModel nodeTree)
    {
        using var stack = new PooledList<NodeTreeModel>();

        IHierarchical? current = nodeTree;

        while (current != null)
        {
            if (current is NodeTreeModel curNodeTree)
            {
                stack.Insert(0, curNodeTree);
            }

            try
            {
                current = current?.HierarchicalParent;
            }
            catch
            {
                current = null;
            }
        }

        using var list = new PooledList<NodeTreeNavigationItem>(stack.Count);

        foreach (NodeTreeModel item in stack.Span)
        {
            NodeTreeNavigationItem? foundItem = FindItem(item);

            foundItem ??= new NodeTreeNavigationItem(
                item.GetObservable(CoreObject.NameProperty).ToReadOnlyReactivePropertySlim()!,
                nodeTree);

            list.Add(foundItem);
        }

        foreach (NodeTreeNavigationItem item in Items.ExceptBy(stack, x => x.NodeTree))
        {
            item.Dispose();
        }

        Items.Clear();
        Items.AddRange(list);

        if (Items.Count > 0)
        {
            NodeTree.Value = Items[^1].ViewModel;
        }
    }

    public void ReadFromJson(JsonObject json)
    {
        if (Layer.Value == null
            && json?.TryGetPropertyValue("layer-filename", out JsonNode? filenameNode) == true
            && (filenameNode as JsonValue)?.TryGetValue(out string? filename) == true
            && filename != null)
        {
            Layer.Value = _editViewModel.Scene.Children.FirstOrDefault(x => x.FileName == filename);
        }
    }

    public void WriteToJson(JsonObject json)
    {
        if (Layer.Value is { FileName: { } filename })
        {
            json["layer-filename"] = filename;
        }
    }

    public object? GetService(Type serviceType)
    {
        if (serviceType == typeof(Element))
            return Layer.Value;

        return _editViewModel.GetService(serviceType);
    }
}
