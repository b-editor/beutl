using System.Diagnostics.CodeAnalysis;

using Avalonia.Controls;

using Beutl.Editor.Components.CurvesTab.ViewModels;
using Beutl.Editor.Components.CurvesTab.Views;

namespace Beutl.Editor.Components.CurvesTab;

[PrimitiveImpl]
public sealed class CurvesTabExtension : ToolTabExtension
{
    public static readonly CurvesTabExtension Instance = new();

    public override bool CanMultiple => true;

    public override string Name => "Curves Tab";

    public override string DisplayName => Strings.Curves;

    public override string Header => Strings.Curves;

    public override bool TryCreateContent(IEditorContext editorContext, [NotNullWhen(true)] out Control? control)
    {
        control = new CurvesTabView();
        return true;
    }

    public override bool TryCreateContext(IEditorContext editorContext, [NotNullWhen(true)] out IToolContext? context)
    {
        context = new CurvesTabViewModel(editorContext);
        return true;
    }
}
