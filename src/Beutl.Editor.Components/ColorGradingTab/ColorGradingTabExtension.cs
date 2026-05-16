using System.Diagnostics.CodeAnalysis;

using Avalonia.Controls;

using Beutl.Editor;
using Beutl.Editor.Components.ColorGradingTab.ViewModels;
using Beutl.Editor.Components.ColorGradingTab.Views;

namespace Beutl.Editor.Components.ColorGradingTab;

[PrimitiveImpl]
public sealed class ColorGradingTabExtension : ToolTabExtension
{
    public static readonly ColorGradingTabExtension Instance = new();

    public override bool CanMultiple => true;

    public override string Name => "Color Grading Tab";

    public override string DisplayName => Strings.ColorGrading;

    public override string Header => Strings.ColorGrading;

    public override bool TryCreateContent(IEditorContext editorContext, [NotNullWhen(true)] out Control? control)
    {
        if (editorContext is ISceneEditorContext)
        {
            control = new ColorGradingTabView();
            return true;
        }

        control = null;
        return false;
    }

    public override bool TryCreateContext(IEditorContext editorContext, [NotNullWhen(true)] out IToolContext? context)
    {
        if (editorContext is ISceneEditorContext sceneContext)
        {
            context = new ColorGradingTabViewModel(sceneContext);
            return true;
        }

        context = null;
        return false;
    }
}
