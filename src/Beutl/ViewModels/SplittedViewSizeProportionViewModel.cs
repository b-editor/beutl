using System.Text.Json.Nodes;
using Reactive.Bindings;

namespace Beutl.ViewModels;

public sealed class SplittedViewSizeProportionViewModel : IJsonSerializable
{
    public ReactivePropertySlim<double> First { get; } = new(1 / 2d);

    public ReactivePropertySlim<double> Second { get; } = new(1 / 2d);

    public void WriteToJson(JsonObject json)
    {
        json[nameof(First)] = First.Value;
        json[nameof(Second)] = Second.Value;
    }

    public void ReadFromJson(JsonObject json)
    {
        if (json.TryGetPropertyValueAsJsonValue(nameof(First), out double first))
            First.Value = first;

        if (json.TryGetPropertyValueAsJsonValue(nameof(Second), out double second))
            Second.Value = second;
    }
}
