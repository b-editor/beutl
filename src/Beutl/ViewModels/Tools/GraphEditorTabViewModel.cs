using System.Text.Json.Nodes;

using Beutl.Framework;
using Beutl.Services.PrimitiveImpls;

using Reactive.Bindings;

namespace Beutl.ViewModels.Tools;

public sealed class GraphEditorTabViewModel : IToolContext
{
    public string Header => "Graph Editor";

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

    public void ReadFromJson(JsonNode json)
    {
    }

    public void WriteToJson(ref JsonNode json)
    {
    }
}
