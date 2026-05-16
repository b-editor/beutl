using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using Beutl.Editor;
using Beutl.Editor.Components.ElementPropertyTab.ViewModels;
using Beutl.Editor.Components.ElementPropertyTab.Views;

namespace Beutl.Editor.Components.ElementPropertyTab;

[PrimitiveImpl]
public sealed class ElementPropertyTabExtension : ToolTabExtension
{
    public static readonly ElementPropertyTabExtension Instance = new();

    public override bool CanMultiple => false;

    public override string Name => "Element Property";

    public override string DisplayName => "Element Property";

    public override string? Header => Strings.ElementProperty;

    public override DockAnchor DefaultAnchor => DockAnchor.Right;

    public override bool OpenByDefault => true;

    public override int DefaultOrder => 0;

    public override bool TryCreateContent(IEditorContext editorContext, [NotNullWhen(true)] out Control? control)
    {
        if (editorContext is ISceneEditorContext)
        {
            control = new ElementPropertyTabView();
            return true;
        }

        control = null;
        return false;
    }

    public override bool TryCreateContext(IEditorContext editorContext, [NotNullWhen(true)] out IToolContext? context)
    {
        if (editorContext is ISceneEditorContext sceneContext)
        {
            context = new ElementPropertyTabViewModel(sceneContext);
            return true;
        }

        context = null;
        return false;
    }
}
