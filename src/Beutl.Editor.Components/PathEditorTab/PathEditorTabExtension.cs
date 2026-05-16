using System.Diagnostics.CodeAnalysis;

using Avalonia.Controls;

using Beutl.Editor;
using Beutl.Editor.Components.PathEditorTab.ViewModels;
using Beutl.Editor.Components.PathEditorTab.Views;

namespace Beutl.Editor.Components.PathEditorTab;

[PrimitiveImpl]
public sealed class PathEditorTabExtension : ToolTabExtension
{
    public static readonly PathEditorTabExtension Instance = new();

    public override string Name => "PathEditor";

    public override string DisplayName => Strings.PathEditor;

    public override bool CanMultiple => false;

    public override string? Header => Strings.PathEditor;

    public override bool TryCreateContent(IEditorContext editorContext, [NotNullWhen(true)] out Control? control)
    {
        if (editorContext is ISceneEditorContext)
        {
            control = new PathEditorTabView();
            return true;
        }

        control = null;
        return false;
    }

    public override bool TryCreateContext(IEditorContext editorContext, [NotNullWhen(true)] out IToolContext? context)
    {
        if (editorContext is ISceneEditorContext sceneContext)
        {
            context = new PathEditorTabViewModel(sceneContext);
            return true;
        }

        context = null;
        return false;
    }
}
