using System.Diagnostics.CodeAnalysis;

using Avalonia.Controls;

using Beutl.Editor.Components.WebBrowserTab.ViewModels;
using Beutl.Editor.Components.WebBrowserTab.Views;

namespace Beutl.Editor.Components.WebBrowserTab;

[PrimitiveImpl]
public sealed class WebBrowserTabExtension : ToolTabExtension
{
    public static readonly WebBrowserTabExtension Instance = new();

    public override string Name => "WebBrowser";

    public override string DisplayName => Strings.WebBrowser;

    public override string? Header => Strings.WebBrowser;

    public override bool CanMultiple => true;

    public override bool ReuseContentAcrossActivation => true;

    public override DockAnchor DefaultAnchor => DockAnchor.Right;

    public override bool TryCreateContent(IEditorContext editorContext, [NotNullWhen(true)] out Control? control)
    {
        control = new WebBrowserTabView();
        return true;
    }

    public override bool TryCreateContext(IEditorContext editorContext, [NotNullWhen(true)] out IToolContext? context)
    {
        context = new WebBrowserTabViewModel(editorContext);
        return true;
    }
}
