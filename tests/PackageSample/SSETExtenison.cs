using System.Diagnostics.CodeAnalysis;
using System.Reactive.Linq;
using System.Text.Json.Nodes;

using Avalonia.Controls;
using Avalonia.Layout;

using Beutl.Extensibility;
using FluentAvalonia.UI.Controls;
using Reactive.Bindings;

namespace PackageSample;

// SampleSceneEditorTabExtenison
[Export]
public sealed class SSETExtenison : ToolTabExtension
{
    public override bool CanMultiple => true;

    public override string Name => "Sample tab";

    public override string DisplayName => "Sample tab";

    public override string Header => "Sample tab";

    public override IconSource GetIcon()
    {
        return new SymbolIconSource
        {
            Symbol = Symbol.Accept
        };
    }

    public override bool TryCreateContent(IEditorContext editorContext, [NotNullWhen(true)] out Control? control)
    {
        control = new TextBlock()
        {
            Text = "Hello world!",
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        return true;
    }

    public override bool TryCreateContext(IEditorContext editorContext, [NotNullWhen(true)] out IToolContext? context)
    {
        context = new Context(this);
        return true;
    }

    private sealed class Context(ToolTabExtension extension) : IToolContext
    {
        public ToolTabExtension Extension { get; } = extension;

        public IReactiveProperty<bool> IsSelected { get; } = new ReactiveProperty<bool>();

        public string Header => "Sample tab";

        public IReactiveProperty<TabPlacement> Placement { get; } =
            new ReactivePropertySlim<TabPlacement>(TabPlacement.LeftLowerBottom);

        public IReactiveProperty<TabDisplayMode> DisplayMode { get; } =
            new ReactivePropertySlim<TabDisplayMode>();

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
}
