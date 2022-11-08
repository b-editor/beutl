using Beutl.Framework;
using Beutl.ViewModels;

using FluentAvalonia.UI.Controls;

using Symbol = FluentIcons.Common.Symbol;
using SymbolIconSource = FluentIcons.FluentAvalonia.SymbolIconSource;

namespace Beutl.Services.PrimitiveImpls;

[PrimitiveImpl]
public sealed class SettingsPageExtension : PageExtension<Pages.SettingsPage, SettingsPageViewModel>
{
    public static readonly SettingsPageExtension Instance = new();

    public override string Name => "SettingsPage";

    public override string DisplayName => "SettingsPage";

    public override IconSource GetFilledIcon()
    {
        return new SymbolIconSource()
        {
            Symbol = Symbol.Settings,
            IsFilled = true
        };
    }

    public override IconSource GetRegularIcon()
    {
        return new SymbolIconSource()
        {
            Symbol = Symbol.Settings
        };
    }
}
