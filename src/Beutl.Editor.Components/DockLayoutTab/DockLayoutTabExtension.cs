using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using Beutl.Editor.Components.DockLayoutTab.ViewModels;
using Beutl.Editor.Components.DockLayoutTab.Views;
using Beutl.Editor.Components.Helpers;
using Beutl.Serialization;
using FluentAvalonia.UI.Controls;
using FluentIconSource = FluentIcons.Avalonia.Fluent.FluentIconSource;
using Icon = FluentIcons.Common.Icon;

namespace Beutl.Editor.Components.DockLayoutTab;

// Moved from the app; layouts and pins saved before name the former type.
[FormerPersistedName("[Beutl].Services.PrimitiveImpls:DockLayoutTabExtension")]
public sealed partial class DockLayoutTabExtension : ToolTabExtension
{
    public static readonly DockLayoutTabExtension Instance = new();

    public override string Name => "Dock layout";

    public override string DisplayName => Strings.DockLayout;

    public override string? Header => Strings.DockLayout;

    public override bool CanMultiple => false;

    public override DockAnchor DefaultAnchor => DockAnchor.Right;

    public override bool OpenByDefault => false;

    public override int DefaultOrder => 110;

    public override FAIconSource? GetIcon()
    {
        return new FluentIconSource { Icon = Icon.BoardSplit };
    }

    public override bool TryCreateContent(IEditorContext editorContext, [NotNullWhen(true)] out Control? control)
    {
        if (HasDock(editorContext))
        {
            control = new DockLayoutView();
            return true;
        }

        control = null;
        return false;
    }

    public override bool TryCreateContext(IEditorContext editorContext, [NotNullWhen(true)] out IToolContext? context)
    {
        if (HasDock(editorContext))
        {
            context = new DockLayoutViewModel(editorContext);
            return true;
        }

        context = null;
        return false;
    }

    private static bool HasDock(IEditorContext? editorContext)
        => editorContext?.GetService(typeof(IToolDockHost)) is IToolDockHost;
}
