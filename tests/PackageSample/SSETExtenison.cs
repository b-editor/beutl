using System.Diagnostics.CodeAnalysis;
using System.Reactive.Linq;
using System.Text.Json.Nodes;

using Avalonia.Controls;
using Avalonia.Layout;

using Beutl.Framework;

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

    public override bool TryCreateContent(IEditorContext editorContext, [NotNullWhen(true)] out IControl? control)
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

    private sealed class Context : IToolContext
    {
        public Context(ToolTabExtension extension)
        {
            Extension = extension;
            IsSelected = new ReactiveProperty<bool>();
        }

        public ToolTabExtension Extension { get; }

        public IReactiveProperty<bool> IsSelected { get; }

        public IReadOnlyReactiveProperty<string> Header { get; } = new ReactivePropertySlim<string>("Sample tab");

        public TabPlacement Placement => TabPlacement.Bottom;

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
