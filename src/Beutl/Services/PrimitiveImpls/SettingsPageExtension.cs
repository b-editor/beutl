using Avalonia.Controls;

using Beutl.Framework;

using FluentAvalonia.UI.Controls;

using Symbol = FluentIcons.Common.Symbol;
using SymbolIconSource = FluentIcons.FluentAvalonia.SymbolIconSource;

namespace Beutl.Services.PrimitiveImpls;

[PrimitiveImpl]
public sealed class SettingsPageExtension : PageExtension
{
    public static readonly SettingsPageExtension Instance = new();

    public override string Name => "SettingsPage";

    public override string DisplayName => "SettingsPage";

    public override IPageContext CreateContext()
    {
        throw new InvalidOperationException();
    }

    public override Control CreateControl()
    {
        return new Pages.SettingsPage();
    }

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
