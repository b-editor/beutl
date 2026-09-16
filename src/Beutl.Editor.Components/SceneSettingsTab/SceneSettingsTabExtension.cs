using System.Diagnostics.CodeAnalysis;

using Avalonia.Controls;

using Beutl.Editor.Components.SceneSettingsTab.ViewModels;
using Beutl.Editor.Components.SceneSettingsTab.Views;
using FluentAvalonia.UI.Controls;
using FluentIconSource = FluentIcons.Avalonia.Fluent.FluentIconSource;
using Icon = FluentIcons.Common.Icon;

namespace Beutl.Editor.Components.SceneSettingsTab;

[PrimitiveImpl]
public sealed class SceneSettingsTabExtension : ToolTabExtension
{
    public static readonly SceneSettingsTabExtension Instance = new();

    public override bool CanMultiple => false;

    public override string Name => "Scene settings";

    public override string DisplayName => Name;

    public override DockAnchor DefaultAnchor => DockAnchor.Right;

    public override int DefaultOrder => 3;

    public override FAIconSource? GetIcon()
    {
        return new FluentIconSource { Icon = Icon.PlaySettings };
    }

    public override bool TryCreateContent(IEditorContext editorContext, [NotNullWhen(true)] out Control? control)
    {
        control = new SceneSettingsTabView();
        return true;
    }

    public override bool TryCreateContext(IEditorContext editorContext, [NotNullWhen(true)] out IToolContext? context)
    {
        context = new SceneSettingsTabViewModel(editorContext);
        return true;
    }
}
