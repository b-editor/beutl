using Avalonia.Controls;

using Beutl.Framework;

using FluentAvalonia.UI.Controls;

using Symbol = FluentIcons.Common.Symbol;
using SymbolIconSource = FluentIcons.FluentAvalonia.SymbolIconSource;

namespace Beutl.Services.PrimitiveImpls;

[PrimitiveImpl]
public sealed class ExtensionsPageExtension : PageExtension
{
    public static readonly ExtensionsPageExtension Instance = new();

    public override string Name => "ExtensionsPage";

    public override string DisplayName => "ExtensionsPage";

    public override IPageContext CreateContext()
    {
        throw new InvalidOperationException();
    }

    public override IControl CreateControl()
    {
        return new Pages.ExtensionsPage();
    }

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
