using System.Text.Json.Nodes;
using Beutl.Collections.Pooled;
using Beutl.NodeGraph;
using Beutl.ProjectSystem;
using Microsoft.Extensions.DependencyInjection;
using Reactive.Bindings;

namespace Beutl.Editor.Components.NodeGraphTab.ViewModels;

public sealed class NodeGraphNavigationItem : IDisposable, IJsonSerializable
{
    internal Lazy<NodeGraphViewModel> _lazyViewModel;

    public NodeGraphNavigationItem(NodeGraphViewModel viewModel, ReadOnlyReactivePropertySlim<string> name, GraphModel nodeGraph)
    {
        _lazyViewModel = new Lazy<NodeGraphViewModel>(viewModel);
        Name = name;
        NodeGraph = nodeGraph;
    }

    public NodeGraphNavigationItem(ReadOnlyReactivePropertySlim<string> name, GraphModel nodeGraph, IEditorContext editorContext)
    {
        NodeGraph = nodeGraph;
        _lazyViewModel = new Lazy<NodeGraphViewModel>(() => new NodeGraphViewModel(NodeGraph, editorContext));
        Name = name;
    }

    public NodeGraphViewModel ViewModel => _lazyViewModel.Value;

    public ReadOnlyReactivePropertySlim<string> Name { get; }

    public GraphModel NodeGraph { get; private set; }

    public void Dispose()
    {
        if (_lazyViewModel.IsValueCreated)
        {
            _lazyViewModel.Value.Dispose();
        }

        Name.Dispose();
        _lazyViewModel = null!;
        NodeGraph = null!;
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

public sealed class NodeGraphTabViewModel : IToolContext
{
    private readonly ReactiveProperty<bool> _isSelected = new(false);
    private readonly CompositeDisposable _disposables = [];
    private IEditorContext _editorContext;

    public NodeGraphTabViewModel(IEditorContext editorContext)
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

                foreach (NodeGraphNavigationItem item in Items)
                {
                    item.Dispose();
                }

                Items.Clear();

                NodeGraph.Value?.Dispose();
                NodeGraph.Value = null;

                if (newModel != null)
                {
                    NodeGraph.Value = new NodeGraphViewModel(newModel, editorContext);
                    var element = newModel.FindHierarchicalParent<Element>();
                    IObservable<string> name = element?.GetObservable(CoreObject.NameProperty) ?? Observable.ReturnThenNever(string.Empty);
                    string? fileName = Path.GetFileNameWithoutExtension(element?.Uri!.LocalPath);

                    Items.Add(new NodeGraphNavigationItem(
                        viewModel: NodeGraph.Value,
                        nodeGraph: newModel,
                        name: name
                            .Select(x => string.IsNullOrWhiteSpace(x) ? fileName : x)
                            .ToReadOnlyReactivePropertySlim()!));

                    RestoreState(newModel);
                }
            }).DisposeWith(_disposables);
    }

    public string Header => Strings.NodeGraph;

    public ToolTabExtension Extension => NodeGraphTabExtension.Instance;

    public IReactiveProperty<bool> IsSelected => _isSelected;

    public ReactivePropertySlim<GraphModel?> Model { get; } = new();

    public ReactivePropertySlim<NodeGraphViewModel?> NodeGraph { get; } = new();

    public CoreList<NodeGraphNavigationItem> Items { get; } = [];

    public void Dispose()
    {
        Model.Value = null;

        _disposables.Dispose();
        foreach (NodeGraphNavigationItem item in Items)
        {
            item.Dispose();
        }

        Items.Clear();
        NodeGraph.Dispose();
        NodeGraph.Value?.Dispose();
        NodeGraph.Value = null;
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

            NodeGraph.Value = Items[index].ViewModel;
        }
    }

    public NodeGraphNavigationItem? FindItem(GraphModel graph)
    {
        foreach (NodeGraphNavigationItem navItem in Items)
        {
            if (navItem.NodeGraph == graph)
            {
                return navItem;
            }
        }

        return null;
    }

    public void NavigateTo(GraphModel graph)
    {
        using var stack = new PooledList<GraphModel>();

        IHierarchical? current = graph;

        while (current != null)
        {
            if (current is GraphModel currentGraph)
            {
                stack.Insert(0, currentGraph);
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

        using var list = new PooledList<NodeGraphNavigationItem>(stack.Count);

        foreach (GraphModel item in stack.Span)
        {
            NodeGraphNavigationItem? foundItem = FindItem(item);

            foundItem ??= new NodeGraphNavigationItem(
                item.GetObservable(CoreObject.NameProperty).ToReadOnlyReactivePropertySlim()!,
                graph,
                _editorContext);

            list.Add(foundItem);
        }

        foreach (NodeGraphNavigationItem item in Items.ExceptBy(stack, x => x.NodeGraph))
        {
            item.Dispose();
        }

        Items.Clear();
        Items.AddRange(list);

        if (Items.Count > 0)
        {
            NodeGraph.Value = Items[^1].ViewModel;
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

    private void SaveState(GraphModel model)
    {
        string viewStateDir = ViewStateDirectory();

        var itemsJson = new JsonObject();
        foreach (NodeGraphNavigationItem item in Items)
        {
            var itemJson = new JsonObject();
            item.WriteToJson(itemJson);
            itemsJson[item.NodeGraph.Id.ToString()] = itemJson;
        }

        var json = new JsonObject { [nameof(Items)] = itemsJson };
        if (NodeGraph.Value != null)
        {
            json["Selected"] = NodeGraph.Value.NodeGraph.Id;
        }

        json.JsonSave(Path.Combine(viewStateDir, $"{model.Id}.nodetree.config"));
    }

    private void RestoreState(GraphModel model)
    {
        string viewStateDir = ViewStateDirectory();
        string viewStateFile = Path.Combine(viewStateDir, $"{model.Id}.nodetree.config");

        if (File.Exists(viewStateFile))
        {
            using var stream = new FileStream(viewStateFile, FileMode.Open, FileAccess.Read, FileShare.Read);
            JsonObject json = JsonNode.Parse(stream)!.AsObject();
            Guid? selected = (Guid?)json["Selected"];
            if (selected.HasValue
                && model.FindById(selected.Value) is GraphModel selectedModel)
            {
                NavigateTo(selectedModel);
            }

            JsonObject itemsJson = json[nameof(Items)]!.AsObject();

            foreach (NodeGraphNavigationItem item in Items)
            {
                if (itemsJson.TryGetPropertyValue(item.NodeGraph.Id.ToString(), out JsonNode? itemJson))
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
            Model.Value = scene.FindById(id) as GraphModel;
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

    internal static GraphModel? FindGraphModel(Element element)
    {
        foreach (var obj in element.Objects)
        {
            if (obj is NodeGraphDrawable drawable)
                return drawable.Model.CurrentValue;
        }

        return null;
    }
}
