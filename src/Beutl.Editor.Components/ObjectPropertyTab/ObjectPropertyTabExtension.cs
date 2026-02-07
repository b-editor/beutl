using System.Diagnostics.CodeAnalysis;

using Avalonia.Controls;

using Beutl.Editor.Components.ObjectPropertyTab.ViewModels;
using Beutl.Editor.Components.ObjectPropertyTab.Views;
using Beutl.Extensibility;

using FluentAvalonia.UI.Controls;

using Symbol = FluentIcons.Common.Symbol;
using SymbolIconSource = FluentIcons.FluentAvalonia.SymbolIconSource;

namespace Beutl.Editor.Components.ObjectPropertyTab;

[PrimitiveImpl]
public sealed class ObjectPropertyTabExtension : ToolTabExtension
{
    public static readonly ObjectPropertyTabExtension Instance = new();

    public override bool CanMultiple => false;

    public override string Name => "Property editor";

    public override string DisplayName => "Property editor";

    public override string? Header => Strings.Properties;

    public override IconSource GetIcon()
    {
        return new SymbolIconSource { Symbol = Symbol.WrenchScrewdriver };
    }

    public override bool TryCreateContent(IEditorContext editorContext, [NotNullWhen(true)] out Control? control)
    {
        control = new ObjectPropertyTabView();
        return true;
    }

    public override bool TryCreateContext(IEditorContext editorContext, [NotNullWhen(true)] out IToolContext? context)
    {
        context = new ObjectPropertyTabViewModel(editorContext);
        return true;
    }
}
