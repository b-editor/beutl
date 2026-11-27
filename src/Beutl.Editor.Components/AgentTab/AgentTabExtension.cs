using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using Beutl.Editor.Components.AgentTab.Views;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.DependencyInjection;
using FluentIconSource = FluentIcons.Avalonia.Fluent.FluentIconSource;
using Icon = FluentIcons.Common.Icon;

namespace Beutl.Editor.Components.AgentTab;

public sealed class AgentTabExtension : ToolTabExtension
{
    public static readonly AgentTabExtension Instance = new();

    public override string Name => "Agent";
    public override string DisplayName => AgentStrings.Title;
    public override string Header => AgentStrings.Title;
    public override bool CanMultiple => true;
    public override DockAnchor DefaultAnchor => DockAnchor.Right;
    public override bool OpenByDefault => false;
    public override int DefaultOrder => 91;

    public override FAIconSource GetIcon() => new FluentIconSource { Icon = Icon.Bot };

    public override bool TryCreateContent(IEditorContext editorContext, [NotNullWhen(true)] out Control? control)
    {
        control = editorContext.GetService<IAgentWorkspaceHost>() is not null ? new AgentTabView() : null;
        return control is not null;
    }

    public override bool TryCreateContext(IEditorContext editorContext, [NotNullWhen(true)] out IToolContext? context)
    {
        context = editorContext.GetService<IAgentWorkspaceHost>()?.CreateWorkspace(editorContext);
        return context is not null;
    }
}
