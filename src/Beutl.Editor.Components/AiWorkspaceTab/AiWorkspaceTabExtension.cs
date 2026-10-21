using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using Beutl.Editor.Components.AiWorkspaceTab.Views;
using Beutl.Serialization;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.DependencyInjection;
using FluentIconSource = FluentIcons.Avalonia.Fluent.FluentIconSource;
using Icon = FluentIcons.Common.Icon;

namespace Beutl.Editor.Components.AiWorkspaceTab;

// Moved from the app; layouts and pins saved before name the former type.
[FormerPersistedName("[Beutl].Services.PrimitiveImpls:AiWorkspaceTabExtension")]
public sealed class AiWorkspaceTabExtension : ToolTabExtension
{
    public static readonly AiWorkspaceTabExtension Instance = new();

    public override string Name => "AI";

    public override string DisplayName => Strings.Ai;

    public override string? Header => Strings.Ai;

    // Several AI tabs can be open at once so two pages can sit side by side. Each
    // tab keeps its own work, so a second one is a second workbench, not a mirror.
    public override bool CanMultiple => true;

    public override DockAnchor DefaultAnchor => DockAnchor.Right;

    public override bool OpenByDefault => false;

    public override int DefaultOrder => 90;

    public override FAIconSource? GetIcon()
    {
        return new FluentIconSource { Icon = Icon.Sparkle };
    }

    public override bool TryCreateContent(
        IEditorContext editorContext,
        [NotNullWhen(true)] out Control? control)
    {
        if (editorContext.GetService<IAiWorkspaceHost>() is not null)
        {
            control = new AiWorkspaceView();
            return true;
        }

        control = null;
        return false;
    }

    public override bool TryCreateContext(
        IEditorContext editorContext,
        [NotNullWhen(true)] out IToolContext? context)
    {
        if (editorContext.GetService<IAiWorkspaceHost>()?.CreateWorkspace(editorContext) is { } workspace)
        {
            context = workspace;
            return true;
        }

        context = null;
        return false;
    }
}
