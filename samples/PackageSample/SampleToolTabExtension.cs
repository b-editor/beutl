using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;

using Avalonia.Controls;
using Avalonia.Layout;

using Beutl.Extensibility;
using Reactive.Bindings;

namespace PackageSample;

// SampleSceneEditorTabExtenison
[Export]
public sealed class SampleToolTabExtension : ToolTabExtension
{
    public override bool CanMultiple => true;

    public override string Name => "Sample tab";

    public override string DisplayName => "Sample tab";

    public override string Header => "Sample tab";

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
        // CanMultiple is true, so the title has to say which instance this is. A real tool would use
        // whatever its tab is showing; there is nothing to show here, hence the counter.
        private static int s_lastInstanceNumber;

        public ToolTabExtension Extension { get; } = extension;

        public IReactiveProperty<bool> IsSelected { get; } = new ReactiveProperty<bool>();

        public IReadOnlyReactiveProperty<string> Header { get; } =
            new ReactivePropertySlim<string>($"Sample tab {Interlocked.Increment(ref s_lastInstanceNumber)}");

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
