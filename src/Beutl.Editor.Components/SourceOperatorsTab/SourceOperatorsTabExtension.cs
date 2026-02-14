using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using Beutl.Editor.Components.SourceOperatorsTab.ViewModels;
using Beutl.Editor.Components.SourceOperatorsTab.Views;
using FluentAvalonia.UI.Controls;
using Symbol = FluentIcons.Common.Symbol;
using SymbolIconSource = FluentIcons.FluentAvalonia.SymbolIconSource;

namespace Beutl.Editor.Components.SourceOperatorsTab;

[PrimitiveImpl]
public sealed class SourceOperatorsTabExtension : ToolTabExtension
{
    public static readonly SourceOperatorsTabExtension Instance = new();

    public override bool CanMultiple => false;

    public override string Name => "Operators editor";

    public override string DisplayName => "Operators editor";

    public override string? Header => Strings.SourceOperators;

    public override IconSource GetIcon()
    {
        return new SymbolIconSource { Symbol = Symbol.Wrench };
    }

    public override bool TryCreateContent(IEditorContext editorContext, [NotNullWhen(true)] out Control? control)
    {
        control = new SourceOperatorsTabView();
        return true;
    }

    public override bool TryCreateContext(IEditorContext editorContext, [NotNullWhen(true)] out IToolContext? context)
    {
        context = new SourceOperatorsTabViewModel(editorContext);
        return true;
    }
}
