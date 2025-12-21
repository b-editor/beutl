using System.Diagnostics.CodeAnalysis;

using Avalonia.Controls;
using Beutl.ViewModels;
using Beutl.ViewModels.Tools;
using Beutl.Views.Tools;
using FluentAvalonia.UI.Controls;
using SymbolIconSource = FluentIcons.FluentAvalonia.SymbolIconSource;
using Symbol = FluentIcons.Common.Symbol;

namespace Beutl.Services.PrimitiveImpls;

[PrimitiveImpl]
public sealed class CurvesTabExtension : ToolTabExtension
{
    public static readonly CurvesTabExtension Instance = new();

    public override bool CanMultiple => true;

    public override string Name => "Curves Tab";

    public override string DisplayName => Strings.Curves;

    public override string Header => Strings.Curves;

    public override IconSource GetIcon()
    {
        return new SymbolIconSource { Symbol = Symbol.Edit };
    }

    public override bool TryCreateContent(IEditorContext editorContext, [NotNullWhen(true)] out Control? control)
    {
        if (editorContext is EditViewModel)
        {
            control = new CurvesTab();
            return true;
        }
        else
        {
            control = null;
            return false;
        }
    }

    public override bool TryCreateContext(IEditorContext editorContext, [NotNullWhen(true)] out IToolContext? context)
    {
        if (editorContext is EditViewModel editViewModel)
        {
            context = new CurvesTabViewModel(editViewModel);
            return true;
        }
        else
        {
            context = null;
            return false;
        }
    }
}
