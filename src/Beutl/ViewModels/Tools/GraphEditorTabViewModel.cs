using System.Text.Json.Nodes;
using Beutl.Animation;
using Beutl.Operation;
using Beutl.ProjectSystem;
using Beutl.Services;
using Beutl.Services.PrimitiveImpls;
using Reactive.Bindings;

namespace Beutl.ViewModels.Tools;

public sealed record GraphEditorItemViewModel(string Name, KeyFrameAnimation Object);

public sealed class GraphEditorTabViewModel : IToolContext
{
    private readonly EditViewModel _editViewModel;
    private readonly CompositeDisposable _disposables = [];

    public GraphEditorTabViewModel(EditViewModel editViewModel)
    {
        _editViewModel = editViewModel;
        Element.Subscribe(_ => Refresh()).DisposeWith(_disposables);
        SelectedAnimation = SelectedItem.CombineLatest(Element)
            .Select(t =>
            {
                if (t.First == null || t.Second == null) return null;

                Type type = t.First.Object.Property.PropertyType;
                Type viewModelType = typeof(GraphEditorViewModel<>).MakeGenericType(type);
                return (GraphEditorViewModel)Activator.CreateInstance(viewModelType, _editViewModel, t.First.Object, t.Second)!;
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

    public ToolTabExtension.TabPlacement Placement { get; } = ToolTabExtension.TabPlacement.Bottom;

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
        var searcher = new ObjectSearcher(Element.Value, v => v is KeyFrameAnimation);
        foreach (KeyFrameAnimation anm in searcher.SearchAll().OfType<KeyFrameAnimation>())
        {
            var prop = anm.Property;
            var item = new GraphEditorItemViewModel(
                prop.GetMetadata<CorePropertyMetadata>(prop.OwnerType).DisplayAttribute?.GetName() ?? anm.Property.Name,
                anm);
            tmp.Add(item);
        }

        if (Items.SequenceEqual(tmp)) return;

        Items.Replace(tmp);
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
            if (json.TryGetPropertyValueAsJsonValue("elementId", out Guid elmId)
                && json.TryGetPropertyValueAsJsonValue("animationId", out Guid anmId)
                && _editViewModel.Scene.FindById(elmId) is Element elm)
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
