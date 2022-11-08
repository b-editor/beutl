using Beutl.Framework;
using Beutl.ViewModels;

using FluentAvalonia.UI.Controls;

using Symbol = FluentIcons.Common.Symbol;
using SymbolIconSource = FluentIcons.FluentAvalonia.SymbolIconSource;

namespace Beutl.Services.PrimitiveImpls;

[PrimitiveImpl]
public sealed class ExtensionsPageExtension : PageExtension<Pages.ExtensionsPage, ExtensionsPageViewModel>
{
    public static readonly ExtensionsPageExtension Instance = new();

    public override string Name => "ExtensionsPage";

    public override string DisplayName => "ExtensionsPage";

    public override IconSource GetFilledIcon()
    {
        return new SymbolIconSource()
        {
            Symbol = Symbol.PuzzlePiece,
            IsFilled = true
        };
    }

    public override IconSource GetRegularIcon()
    {
        return new SymbolIconSource()
        {
            Symbol = Symbol.PuzzlePiece
        };
    }
}
