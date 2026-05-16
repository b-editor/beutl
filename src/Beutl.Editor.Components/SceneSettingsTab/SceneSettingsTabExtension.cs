using System.Diagnostics.CodeAnalysis;

using Avalonia.Controls;

using Beutl.Editor;
using Beutl.Editor.Components.SceneSettingsTab.ViewModels;
using Beutl.Editor.Components.SceneSettingsTab.Views;

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

    public override bool TryCreateContent(IEditorContext editorContext, [NotNullWhen(true)] out Control? control)
    {
        if (editorContext is ISceneEditorContext)
        {
            control = new SceneSettingsTabView();
            return true;
        }

        control = null;
        return false;
    }

    public override bool TryCreateContext(IEditorContext editorContext, [NotNullWhen(true)] out IToolContext? context)
    {
        if (editorContext is ISceneEditorContext sceneContext)
        {
            context = new SceneSettingsTabViewModel(sceneContext);
            return true;
        }

        context = null;
        return false;
    }
}
