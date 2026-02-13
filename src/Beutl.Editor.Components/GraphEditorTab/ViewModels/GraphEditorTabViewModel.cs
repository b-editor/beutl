using System.Text.Json.Nodes;
using Beutl.Animation;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.NodeTree;
using Beutl.ProjectSystem;
using Microsoft.Extensions.DependencyInjection;
using Reactive.Bindings;

namespace Beutl.Editor.Components.GraphEditorTab.ViewModels;

public sealed record GraphEditorItemViewModel(string Name, KeyFrameAnimation Object);

public sealed class GraphEditorTabViewModel : IToolContext
{
    private readonly IEditorContext _editorContext;
    private readonly CompositeDisposable _disposables = [];

    public GraphEditorTabViewModel(IEditorContext editorContext)
    {
        _editorContext = editorContext;
        Element.Subscribe(_ => Refresh()).DisposeWith(_disposables);
        SelectedAnimation = SelectedItem.CombineLatest(Element)
            .Select(t =>
            {
                if (t.First == null || t.Second == null) return null;

                Type type = t.First.Object.ValueType;
                Type viewModelType = typeof(GraphEditorViewModel<>).MakeGenericType(type);
                return (GraphEditorViewModel)Activator.CreateInstance(viewModelType, _editorContext, t.First.Object, t.Second)!;
            })
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);
    }

    public string Header => Strings.GraphEditor;

    public ToolTabExtension Extension => GraphEditorTabExtension.Instance;

    public ReadOnlyReactivePropertySlim<GraphEditorViewModel?> SelectedAnimation { get; }

    public ReactivePropertySlim<GraphEditorItemViewModel?> SelectedItem { get; } = new();

    public ReactiveProperty<Element?> Element { get; } = new();

    public CoreList<GraphEditorItemViewModel> Items { get; } = [];

    public IReactiveProperty<bool> IsSelected { get; } = new ReactiveProperty<bool>();

    public IReactiveProperty<ToolTabExtension.TabPlacement> Placement { get; } =
        new ReactivePropertySlim<ToolTabExtension.TabPlacement>(ToolTabExtension.TabPlacement.LeftLowerBottom);

    public IReactiveProperty<ToolTabExtension.TabDisplayMode> DisplayMode { get; } =
        new ReactivePropertySlim<ToolTabExtension.TabDisplayMode>();

    public void Dispose()
    {
        _disposables.Dispose();
    }

    public object? GetService(Type serviceType)
    {
        return null;
    }

    public void Refresh()
    {
        var selected = SelectedItem.Value;
        if (Element.Value == null)
        {
            Items.Clear();
            return;
        }

        var tmp = new List<GraphEditorItemViewModel>();
        var searcher = new ObjectSearcher(Element.Value, v => v is EngineObject);
        foreach (IProperty prop in searcher.SearchAll().OfType<EngineObject>().SelectMany(o => o.Properties))
        {
            var propInfo = prop.GetPropertyInfo();
            if (propInfo == null || prop.Animation is not KeyFrameAnimation anm) continue;

            string name = TypeDisplayHelpers.GetLocalizedName(propInfo);
            var item = new GraphEditorItemViewModel(
                name,
                anm);
            tmp.Add(item);
        }
        // IAutomaticallyGeneratedSocketがついているNodeItemのプロパティからアニメーションを探す
        searcher = new ObjectSearcher(Element.Value, v => v is IAutomaticallyGeneratedSocket);
        foreach (INodeItem socket in searcher.SearchAll().OfType<INodeItem>())
        {
            if (socket.Property is not IAnimatablePropertyAdapter { Animation: KeyFrameAnimation anm, DisplayName: { } displayName })
                continue;

            var item = new GraphEditorItemViewModel(displayName, anm);
            tmp.Add(item);
        }


        if (Items.SequenceEqual(tmp)) return;

        Items.Clear();
        Items.AddRange(tmp);
        SelectedItem.Value = Items.FirstOrDefault(i => i.Object == selected?.Object);
    }

    public void Select(KeyFrameAnimation? animation)
    {
        if (animation == null)
        {
            SelectedItem.Value = null;
        }
        else
        {
            Refresh();
            SelectedItem.Value = Items.FirstOrDefault(i => i.Object == animation);
        }
    }

    public void ReadFromJson(JsonObject json)
    {
        try
        {
            var scene = _editorContext.GetRequiredService<Scene>();
            if (json.TryGetPropertyValueAsJsonValue("elementId", out Guid elmId)
                && json.TryGetPropertyValueAsJsonValue("animationId", out Guid anmId)
                && scene.FindById(elmId) is Element elm)
            {
                Element.Value = elm;
                var searcher = new ObjectSearcher(elm, v => v is KeyFrameAnimation anm && anm.Id == anmId);
                if (searcher.Search() is KeyFrameAnimation anm)
                {
                    Select(anm);
                }
            }
        }
        catch
        {
        }
    }

    public void WriteToJson(JsonObject json)
    {
        if (SelectedAnimation.Value is { Element.Id: { } elmId, Animation: ICoreObject { Id: var anmId } })
        {
            json["elementId"] = elmId;
            json["animationId"] = anmId;
        }
    }
}
