using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using Beutl.Editor;
using Beutl.Editor.Components.NodeGraphTab.ViewModels;
using Beutl.Editor.Components.NodeGraphTab.Views;

namespace Beutl.Editor.Components.NodeGraphTab;

[PrimitiveImpl]
public sealed class NodeGraphTabExtension : ToolTabExtension
{
    public static readonly NodeGraphTabExtension Instance = new();

    public override bool CanMultiple => true;

    public override string Name => "NodeGraphTab";

    public override string DisplayName => "NodeGraph";

    public override string? Header => "GraphNode Tree";

    public override bool TryCreateContent(IEditorContext editorContext, [NotNullWhen(true)] out Control? control)
    {
        control = new NodeGraphTabView();
        return true;
    }

    public override bool TryCreateContext(IEditorContext editorContext, [NotNullWhen(true)] out IToolContext? context)
    {
        if (editorContext is ISceneEditorContext sceneContext)
        {
            context = new NodeGraphTabViewModel(sceneContext);
            return true;
        }

        context = null;
        return false;
    }
}
