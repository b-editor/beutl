using Beutl.Framework;
using Beutl.ViewModels;

using FluentAvalonia.UI.Controls;

using Symbol = FluentIcons.Common.Symbol;
using SymbolIconSource = FluentIcons.FluentAvalonia.SymbolIconSource;

namespace Beutl.Services.PrimitiveImpls;

[PrimitiveImpl]
public sealed class OutputPageExtension : PageExtension
{
    public static readonly OutputPageExtension Instance = new();

    public override Type Control => null!;

    public override Type Context => typeof(OutputPageViewModel);

    public override string Name => "OutputPage";

    public override string DisplayName => "OutputPage";

    public override IconSource GetFilledIcon()
    {
        return new SymbolIconSource()
        {
            Symbol = Symbol.ArrowExportLtr,
            IsFilled = true
        };
    }

    public override IconSource GetRegularIcon()
    {
        return new SymbolIconSource()
        {
            Symbol = Symbol.ArrowExportLtr
        };
    }
}
