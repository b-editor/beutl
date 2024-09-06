using System.Text.Json.Nodes;
using Reactive.Bindings;

namespace Beutl.ViewModels;

public sealed class ReDockSizeProportionViewModel : IJsonSerializable
{
    public ReactivePropertySlim<double> Left { get; } = new(1 / 4d);

    public ReactivePropertySlim<double> Center { get; } = new(1 / 2d);

    public ReactivePropertySlim<double> Right { get; } = new(1 / 4d);

    public void WriteToJson(JsonObject json)
    {
        json[nameof(Left)] = Left.Value;
        json[nameof(Center)] = Center.Value;
        json[nameof(Right)] = Right.Value;
    }

    public void ReadFromJson(JsonObject json)
    {
        if (json.TryGetPropertyValueAsJsonValue(nameof(Left), out double left))
            Left.Value = left;

        if (json.TryGetPropertyValueAsJsonValue(nameof(Center), out double center))
            Center.Value = center;

        if (json.TryGetPropertyValueAsJsonValue(nameof(Right), out double right))
            Right.Value = right;
    }
}
