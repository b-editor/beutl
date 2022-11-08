using System.Text.Json.Nodes;

using Beutl.Animation.Easings;
using Beutl.Framework;
using Beutl.Services.PrimitiveImpls;

using Reactive.Bindings;

namespace Beutl.ViewModels;

public sealed class EasingsViewModel : IToolContext
{
    public EasingsViewModel()
    {
        Header = new ReactivePropertySlim<string>(Strings.Easings);
    }

    public ReactiveCollection<Easing> Easings { get; } = new()
    {
        new BackEaseIn(),
        new BackEaseInOut(),
        new BackEaseOut(),
        new BounceEaseIn(),
        new BounceEaseInOut(),
        new BounceEaseOut(),
        new CircularEaseIn(),
        new CircularEaseInOut(),
        new CircularEaseOut(),
        new CubicEaseIn(),
        new CubicEaseInOut(),
        new CubicEaseOut(),
        new ElasticEaseIn(),
        new ElasticEaseInOut(),
        new ElasticEaseOut(),
        new ExponentialEaseIn(),
        new ExponentialEaseInOut(),
        new ExponentialEaseOut(),
        new QuadraticEaseIn(),
        new QuadraticEaseInOut(),
        new QuadraticEaseOut(),
        new QuarticEaseIn(),
        new QuarticEaseInOut(),
        new QuarticEaseOut(),
        new QuinticEaseIn(),
        new QuinticEaseInOut(),
        new QuinticEaseOut(),
        new SineEaseIn(),
        new SineEaseInOut(),
        new SineEaseOut(),
        new LinearEasing(),
    };

    public ToolTabExtension Extension => EasingsTabExtension.Instance;

    public IReactiveProperty<bool> IsSelected { get; } = new ReactivePropertySlim<bool>();

    public IReadOnlyReactiveProperty<string> Header { get; }

    public ToolTabExtension.TabPlacement Placement => ToolTabExtension.TabPlacement.Right;

    public void Dispose()
    {
        Header.Dispose();
    }

    public void ReadFromJson(JsonNode json)
    {
    }

    public void WriteToJson(ref JsonNode json)
    {
    }
}
