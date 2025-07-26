using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using Beutl.ViewModels;
using Beutl.ViewModels.Tools;
using Beutl.Views.Tools;
using FluentAvalonia.UI.Controls;
using Symbol = FluentIcons.Common.Symbol;
using SymbolIconSource = FluentIcons.FluentAvalonia.SymbolIconSource;

namespace Beutl.Services.PrimitiveImpls;

[PrimitiveImpl]
public sealed class SourceOperatorsTabExtension : ToolTabExtension
{
    public static readonly SourceOperatorsTabExtension Instance = new();

    public override bool CanMultiple => true;

    public override string Name => "Operators editor";

    public override string DisplayName => "Operators editor";

    public override string? Header => Strings.SourceOperators;

    public override IconSource GetIcon()
    {
        return new SymbolIconSource { Symbol = Symbol.Wrench };
    }

    public override bool TryCreateContent(IEditorContext editorContext, [NotNullWhen(true)] out Control? control)
    {
        if (editorContext is EditViewModel)
        {
            control = new SourceOperatorsTab();
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
            context = new SourceOperatorsTabViewModel(editViewModel);
            return true;
        }
        else
        {
            context = null;
            return false;
        }
    }
}
