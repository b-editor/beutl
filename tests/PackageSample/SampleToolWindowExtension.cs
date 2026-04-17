using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using Avalonia.Layout;
using Beutl.Extensibility;
using FluentAvalonia.UI.Controls;

namespace PackageSample;

public sealed class SampleToolWindowContext(SampleToolWindowExtension extension) : IToolWindowContext
{
    public ToolWindowExtension Extension { get; } = extension;

    public string Header => "Mail";

    public void Dispose()
    {
    }
}

[Export]
public sealed class SampleToolWindowExtension : ToolWindowExtension
{
    public override string Name => "Sample tool window";

    public override string DisplayName => "Sample tool window";

    public override ToolWindowMode Mode => ToolWindowMode.Window;

    public override bool CanMultiple => true;

    public override IconSource? GetIcon() => new SymbolIconSource { Symbol = Symbol.Mail };

    public override bool TryCreateContent([NotNullWhen(true)] out Window? window)
    {
        window = new Window
        {
            Width = 400,
            Height = 300,
            Title = "Mail",
            Content = new TextBlock
            {
                Text = "Hello from ToolWindow!",
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
            }
        };
        return true;
    }

    public override bool TryCreateContext([NotNullWhen(true)] out IToolWindowContext? context)
    {
        context = new SampleToolWindowContext(this);
        return true;
    }
}
