using System.Text.Json.Nodes;
using Beutl.Extensibility;
using Reactive.Bindings;

namespace PackageSample;

public sealed class EditWellKnownSizeTabViewModel(ToolTabExtension extension) : IToolContext
{
    public ToolTabExtension Extension { get; } = extension;

    public IReactiveProperty<bool> IsSelected { get; } = new ReactivePropertySlim<bool>();

    public IReactiveProperty<ToolTabExtension.TabPlacement> Placement { get; } =
        new ReactivePropertySlim<ToolTabExtension.TabPlacement>(ToolTabExtension.TabPlacement.RightUpperBottom);

    public IReactiveProperty<ToolTabExtension.TabDisplayMode> DisplayMode { get; } =
        new ReactiveProperty<ToolTabExtension.TabDisplayMode>();

    public string Header => "Edit Well known size";

    public AddWellKnownSizeScreenViewModel AddScreen { get; } = new AddWellKnownSizeScreenViewModel();

    public RemoveWellKnownSizeScreenViewModel RemoveScreen { get; } = new RemoveWellKnownSizeScreenViewModel();

    public void Dispose()
    {
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
