using System.Diagnostics.CodeAnalysis;

using Avalonia.Controls;

using Beutl.Editor.Components.TerminalTab.ViewModels;
using Beutl.Editor.Components.TerminalTab.Views;

namespace Beutl.Editor.Components.TerminalTab;

[PrimitiveImpl]
public sealed class TerminalTabExtension : ToolTabExtension
{
    public static readonly TerminalTabExtension Instance = new();

    public override string Name => "Terminal";

    public override string DisplayName => Strings.Terminal;

    public override string? Header => Strings.Terminal;

    public override bool CanMultiple => true;

    public override DockAnchor DefaultAnchor => DockAnchor.Bottom;

    public override bool TryCreateContent(IEditorContext editorContext, [NotNullWhen(true)] out Control? control)
    {
        control = new TerminalTabView();
        return true;
    }

    public override bool TryCreateContext(IEditorContext editorContext, [NotNullWhen(true)] out IToolContext? context)
    {
        context = new TerminalTabViewModel(editorContext);
        return true;
    }
}
