using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Beutl.ViewModels;
using FluentAvalonia.UI.Controls;
using Symbol = FluentIcons.Common.Symbol;
using SymbolIconSource = FluentIcons.FluentAvalonia.SymbolIconSource;

namespace Beutl.Services.PrimitiveImpls;

[PrimitiveImpl]
public sealed class ExtensionsPageExtension : PageExtension
{
    public static readonly ExtensionsPageExtension Instance = new();

    public override string Name => "ExtensionsPage";

    public override string DisplayName => Strings.Extensions;

    public override IPageContext CreateContext()
    {
        var mainViewModel = Application.Current!.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime
            ? lifetime.MainWindow!.DataContext as MainViewModel
            : throw new InvalidOperationException("ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime");
        return new ExtensionsPageViewModel(mainViewModel!._beutlClients);
    }

    public override Control CreateControl()
    {
        return new Pages.ExtensionsPage();
    }

    [Obsolete]
    public override IconSource GetFilledIcon()
    {
        return new SymbolIconSource() { Symbol = Symbol.PuzzlePiece, IsFilled = true };
    }

    public override IconSource GetRegularIcon()
    {
        return new SymbolIconSource() { Symbol = Symbol.PuzzlePiece };
    }
}
