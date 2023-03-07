using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

using Beutl.Framework;
using Beutl.Services.PrimitiveImpls;

using Reactive.Bindings;

namespace Beutl.ViewModels.Tools;

public sealed class GraphEditorTabViewModel : IToolContext
{
    public GraphEditorTabViewModel()
    {
    }

    public ToolTabExtension Extension => GraphEditorTabExtension.Instance;

    public ReactivePropertySlim<GraphEditorViewModel?> SelectedAnimation { get; } = new();

    public IReactiveProperty<bool> IsSelected { get; } = new ReactiveProperty<bool>();

    public ToolTabExtension.TabPlacement Placement { get; } = ToolTabExtension.TabPlacement.Bottom;

    public void Dispose()
    {
    }

    public void ReadFromJson(JsonNode json)
    {
    }

    public void WriteToJson(ref JsonNode json)
    {
    }
}
