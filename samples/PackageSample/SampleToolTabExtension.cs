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
        // Number each instance because CanMultiple is enabled.
        private static int s_lastInstanceNumber;
        private int _disposed;

        public ToolTabExtension Extension { get; } = extension;

        public IReactiveProperty<bool> IsSelected { get; } = new ReactiveProperty<bool>();

        public IReadOnlyReactiveProperty<string> Header { get; } =
            new ReactivePropertySlim<string>($"Sample tab {Interlocked.Increment(ref s_lastInstanceNumber)}");

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return ValueTask.CompletedTask;

            IsSelected.Dispose();
            (Header as IDisposable)?.Dispose();
            return ValueTask.CompletedTask;
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
