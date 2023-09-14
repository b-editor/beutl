using System.Text.Json.Nodes;

using Beutl.Services.PrimitiveImpls;

using Reactive.Bindings;

namespace Beutl.ViewModels.Tools;

public sealed class GraphEditorTabViewModel : IToolContext
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
    }

    public void WriteToJson(JsonObject json)
    {
    }
}
