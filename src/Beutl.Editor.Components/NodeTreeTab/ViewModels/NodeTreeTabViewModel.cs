using System.Text.Json.Nodes;
using Beutl.Collections.Pooled;
using Beutl.NodeTree;
using Beutl.ProjectSystem;
using Microsoft.Extensions.DependencyInjection;
using Reactive.Bindings;

namespace Beutl.Editor.Components.NodeTreeTab.ViewModels;

public sealed class NodeTreeNavigationItem : IDisposable, IJsonSerializable
{
    internal Lazy<NodeTreeViewModel> _lazyViewModel;

    public NodeTreeNavigationItem(NodeTreeViewModel viewModel, ReadOnlyReactivePropertySlim<string> name, NodeTreeModel nodeTree)
    {
        _lazyViewModel = new Lazy<NodeTreeViewModel>(viewModel);
        Name = name;
        NodeTree = nodeTree;
    }

    public NodeTreeNavigationItem(ReadOnlyReactivePropertySlim<string> name, NodeTreeModel nodeTree, IEditorContext editorContext)
    {
        NodeTree = nodeTree;
        _lazyViewModel = new Lazy<NodeTreeViewModel>(() => new NodeTreeViewModel(NodeTree, editorContext));
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

    public void WriteToJson(JsonObject json)
    {
        ViewModel.WriteToJson(json);
    }

    public void ReadFromJson(JsonObject json)
    {
        ViewModel.ReadFromJson(json);
    }
}

public sealed class NodeTreeTabViewModel : IToolContext
{
    private readonly ReactiveProperty<bool> _isSelected = new(false);
    private readonly CompositeDisposable _disposables = [];
    private IEditorContext _editorContext;
    private NodeTreeModel? _oldModel;

    public NodeTreeTabViewModel(IEditorContext editorContext)
    {
        _editorContext = editorContext;

        Model.CombineWithPrevious()
            .Subscribe(t =>
            {
                var oldModel = t.OldValue;
                var newModel = t.NewValue;
                if (oldModel != null)
                {
                    SaveState(oldModel);
                }

                foreach (NodeTreeNavigationItem item in Items)
                {
                    item.Dispose();
                }

                Items.Clear();

                NodeTree.Value?.Dispose();
                NodeTree.Value = null;

                if (newModel != null)
                {
                    NodeTree.Value = new NodeTreeViewModel(newModel, editorContext);
                    var element = newModel.FindHierarchicalParent<Element>();
                    IObservable<string> name = element?.GetObservable(CoreObject.NameProperty) ?? Observable.ReturnThenNever(string.Empty);
                    string? fileName = Path.GetFileNameWithoutExtension(element?.Uri!.LocalPath);

                    Items.Add(new NodeTreeNavigationItem(
                        viewModel: NodeTree.Value,
                        nodeTree: newModel,
                        name: name
                            .Select(x => string.IsNullOrWhiteSpace(x) ? fileName : x)
                            .ToReadOnlyReactivePropertySlim()!));

                    RestoreState(newModel);
                }
            }).DisposeWith(_disposables);
    }

    public string Header => Strings.NodeTree;

    public ToolTabExtension Extension => NodeTreeTabExtension.Instance;

    public IReactiveProperty<bool> IsSelected => _isSelected;

    public IReactiveProperty<ToolTabExtension.TabPlacement> Placement { get; } =
        new ReactivePropertySlim<ToolTabExtension.TabPlacement>(ToolTabExtension.TabPlacement.LeftLowerBottom);

    public IReactiveProperty<ToolTabExtension.TabDisplayMode> DisplayMode { get; } =
        new ReactivePropertySlim<ToolTabExtension.TabDisplayMode>();

    public ReactivePropertySlim<NodeTreeModel?> Model { get; } = new();

    public ReactivePropertySlim<NodeTreeViewModel?> NodeTree { get; } = new();

    public CoreList<NodeTreeNavigationItem> Items { get; } = [];

    public void Dispose()
    {
        Model.Value = null;

        _disposables.Dispose();
        foreach (NodeTreeNavigationItem item in Items)
        {
            item.Dispose();
        }

        Items.Clear();
        NodeTree.Dispose();
        NodeTree.Value?.Dispose();
        NodeTree.Value = null;
        _editorContext = null!;
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
                nodeTree,
                _editorContext);

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

    private string ViewStateDirectory()
    {
        Scene scene = _editorContext.GetRequiredService<Scene>();
        string directory = Path.GetDirectoryName(scene.Uri!.LocalPath)!;

        directory = Path.Combine(directory, Constants.BeutlFolder, Constants.ViewStateFolder);
        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        return directory;
    }

    private void SaveState(NodeTreeModel model)
    {
        string viewStateDir = ViewStateDirectory();

        var itemsJson = new JsonObject();
        foreach (NodeTreeNavigationItem item in Items)
        {
            var itemJson = new JsonObject();
            item.WriteToJson(itemJson);
            itemsJson[item.NodeTree.Id.ToString()] = itemJson;
        }

        var json = new JsonObject { [nameof(Items)] = itemsJson };
        if (NodeTree.Value != null)
        {
            json["Selected"] = NodeTree.Value.NodeTree.Id;
        }

        json.JsonSave(Path.Combine(viewStateDir, $"{model.Id}.nodetree.config"));
    }

    private void RestoreState(NodeTreeModel model)
    {
        string viewStateDir = ViewStateDirectory();
        string viewStateFile = Path.Combine(viewStateDir, $"{model.Id}.nodetree.config");

        if (File.Exists(viewStateFile))
        {
            using var stream = new FileStream(viewStateFile, FileMode.Open, FileAccess.Read, FileShare.Read);
            JsonObject json = JsonNode.Parse(stream)!.AsObject();
            Guid? selected = (Guid?)json["Selected"];
            if (selected.HasValue
                && model.FindById(selected.Value) is NodeTreeModel selectedModel)
            {
                NavigateTo(selectedModel);
            }

            JsonObject itemsJson = json[nameof(Items)]!.AsObject();

            foreach (NodeTreeNavigationItem item in Items)
            {
                if (itemsJson.TryGetPropertyValue(item.NodeTree.Id.ToString(), out JsonNode? itemJson))
                {
                    item.ReadFromJson(itemJson!.AsObject());
                }
            }
        }
    }

    public void ReadFromJson(JsonObject json)
    {
        Scene scene = _editorContext.GetRequiredService<Scene>();
        if (Model.Value == null
            && json.TryGetPropertyValue("ModelId", out JsonNode? idNode)
            && (idNode as JsonValue)?.TryGetValue(out Guid id) == true)
        {
            Model.Value = scene.FindById(id) as NodeTreeModel;
        }
    }

    public void WriteToJson(JsonObject json)
    {
        if (Model.Value != null)
        {
            json["ModelId"] = Model.Value.Id;
            SaveState(Model.Value);
        }
    }

    public object? GetService(Type serviceType)
    {
        if (serviceType == typeof(Element))
            return Model.Value?.FindHierarchicalParent<Element>();

        return _editorContext.GetService(serviceType);
    }

    internal static NodeTreeModel? FindNodeTreeModel(Element element)
    {
        foreach (var obj in element.Objects)
        {
            if (obj is NodeTreeDrawable drawable)
                return drawable.Model.CurrentValue;
        }

        return null;
    }
}
