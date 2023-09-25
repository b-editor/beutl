using System.Text.Json.Nodes;

using Beutl.Extensibility;

using Reactive.Bindings;

namespace PackageSample;

public sealed class EditWellKnownSizeTabViewModel : IToolContext
{
    public EditWellKnownSizeTabViewModel(ToolTabExtension extension)
    {
        Extension = extension;
        AddScreen = new AddWellKnownSizeScreenViewModel();
        RemoveScreen = new RemoveWellKnownSizeScreenViewModel();
    }

    public ToolTabExtension Extension { get; }

    public IReactiveProperty<bool> IsSelected { get; } = new ReactivePropertySlim<bool>();

    public string Header => "Edit Well known size";

    public ToolTabExtension.TabPlacement Placement => ToolTabExtension.TabPlacement.Right;

    public AddWellKnownSizeScreenViewModel AddScreen { get; }
    
    public RemoveWellKnownSizeScreenViewModel RemoveScreen { get; }

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
