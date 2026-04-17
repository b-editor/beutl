using System.Diagnostics.CodeAnalysis;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Beutl.ViewModels;
using FluentAvalonia.UI.Controls;
using Symbol = FluentIcons.Common.Symbol;
using SymbolIconSource = FluentIcons.FluentAvalonia.SymbolIconSource;

namespace Beutl.Services.PrimitiveImpls;

[PrimitiveImpl]
public sealed class ExtensionsToolWindowExtension : ToolWindowExtension
{
    public static readonly ExtensionsToolWindowExtension Instance = new();

    public override string Name => "ExtensionsToolWindow";

    public override string DisplayName => Strings.Extensions;

    public override ToolWindowMode Mode => ToolWindowMode.Dialog;

    public override IconSource? GetIcon()
        => new SymbolIconSource() { Symbol = Symbol.PuzzlePiece };

    public override bool TryCreateContent([NotNullWhen(true)] out Window? window)
    {
        window = new Pages.ExtensionsPage();
        return true;
    }

    public override bool TryCreateContext([NotNullWhen(true)] out IToolWindowContext? context)
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime lifetime
            || lifetime.MainWindow?.DataContext is not MainViewModel mainViewModel)
        {
            context = null;
            return false;
        }

        context = new ExtensionsPageViewModel(mainViewModel._beutlClients);
        return true;
    }
}
