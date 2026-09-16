using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using Beutl.Editor.Components.ProxiesTab.ViewModels;
using Beutl.Editor.Components.ProxiesTab.Views;
using FluentAvalonia.UI.Controls;
using FluentIconSource = FluentIcons.Avalonia.Fluent.FluentIconSource;
using Icon = FluentIcons.Common.Icon;

namespace Beutl.Editor.Components.ProxiesTab;

[PrimitiveImpl]
public sealed class ProxiesTabExtension : ToolTabExtension
{
    public static readonly ProxiesTabExtension Instance = new();

    public override string Name => "Proxies";

    public override string DisplayName => Strings.Proxies;

    public override string? Header => Strings.Proxies;

    public override bool CanMultiple => false;

    public override DockAnchor DefaultAnchor => DockAnchor.Right;

    public override int DefaultOrder => 4;

    public override FAIconSource? GetIcon()
    {
        return new FluentIconSource { Icon = Icon.VideoClipOptimize };
    }

    public override bool TryCreateContent(IEditorContext editorContext, [NotNullWhen(true)] out Control? control)
    {
        control = new ProxiesTabView();
        return true;
    }

    public override bool TryCreateContext(IEditorContext editorContext, [NotNullWhen(true)] out IToolContext? context)
    {
        context = new ProxiesTabViewModel(editorContext);
        return true;
    }
}
