using Beutl.Framework;
using Beutl.ViewModels;

using FluentAvalonia.UI.Controls;

using Symbol = FluentIcons.Common.Symbol;
using SymbolIconSource = FluentIcons.FluentAvalonia.SymbolIconSource;

namespace Beutl.Services.PrimitiveImpls;

[PrimitiveImpl]
public sealed class EditPageExtension : PageExtension<Pages.EditPage, EditPageViewModel>
{
    public static readonly EditPageExtension Instance = new();

    public override string Name => "EditPage";

    public override string DisplayName => "EditPage";

    public override IconSource GetFilledIcon()
    {
        return new SymbolIconSource()
        {
            Symbol = Symbol.Edit,
            IsFilled = true
        };
    }

    public override IconSource GetRegularIcon()
    {
        return new SymbolIconSource()
        {
            Symbol = Symbol.Edit
        };
    }
}
