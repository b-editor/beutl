using System.Text.Json.Nodes;

using Beutl.Animation;
using Beutl.ProjectSystem;
using Beutl.Services;
using Beutl.Services.PrimitiveImpls;

using Reactive.Bindings;

namespace Beutl.ViewModels.Tools;

public sealed class GraphEditorTabViewModel(EditViewModel editViewModel) : IToolContext
{
    public string Header => Strings.GraphEditor;

    public ToolTabExtension Extension => GraphEditorTabExtension.Instance;

    public ReactivePropertySlim<GraphEditorViewModel?> SelectedAnimation { get; } = new();

    public IReactiveProperty<bool> IsSelected { get; } = new ReactiveProperty<bool>();

    public ToolTabExtension.TabPlacement Placement { get; } = ToolTabExtension.TabPlacement.Bottom;

    public void Dispose()
    {
        SelectedAnimation.Dispose();
        SelectedAnimation.Value?.Dispose();
        SelectedAnimation.Value = null;
    }

    public object? GetService(Type serviceType)
    {
        return null;
    }

    public void ReadFromJson(JsonObject json)
    {
        try
        {
            if (json.TryGetPropertyValueAsJsonValue("elementId", out Guid elmId)
                && json.TryGetPropertyValueAsJsonValue("animationId", out Guid anmId)
                && editViewModel.Scene.FindById(elmId) is Element elm)
            {
                var searcher = new ObjectSearcher(elm, v => v is KeyFrameAnimation kfanm && kfanm.Id == anmId);
                if (searcher.Search() is KeyFrameAnimation kfanm)
                {
                    Type type = kfanm.Property.PropertyType;
                    Type viewModelType = typeof(GraphEditorViewModel<>).MakeGenericType(type);
                    SelectedAnimation.Value = (GraphEditorViewModel)Activator.CreateInstance(viewModelType, editViewModel, kfanm, elm)!;
                }
            }
        }
        catch
        {
        }
    }

    public void WriteToJson(JsonObject json)
    {
        if (SelectedAnimation.Value is { Element.Id: { } elmId, Animation: ICoreObject { Id: { } anmId } })
        {
            json["elementId"] = elmId;
            json["animationId"] = anmId;
        }
    }
}
