using System.CommandLine;
using System.CommandLine.Parsing;
using System.ComponentModel;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

using FluentAvalonia.UI.Windowing;

using Symbol = FluentIcons.Common.Symbol;

namespace Beutl.WaitingDialog;

public partial class MainWindow : AppWindow
{
    private readonly bool _closable;

    public MainWindow()
    {
        InitializeComponent();
        ShowAsDialog = true;
        Topmost = true;
#if DEBUG
        this.AttachDevTools();
#endif
        var titleOption = new Option<string?>("--title", () => null);
        var subtitleOption = new Option<string?>("--subtitle", () => null);
        var iconOption = new Option<string?>("--icon", () => null);
        var contentOption = new Option<string?>("--content", () => null);
        var progressOption = new Option<bool>("--progress", () => false);
        var closableOption = new Option<bool>("--closable", () => false);
        var command = new RootCommand()
        {
            titleOption,
            subtitleOption,
            iconOption,
            contentOption,
            progressOption,
            closableOption
        };

        string[] args = ((ClassicDesktopStyleApplicationLifetime)Application.Current!.ApplicationLifetime!).Args!;
        ParseResult result = command.Parse(args);

        string? title = result.GetValueForOption(titleOption);
        if (title != null)
        {
            headerRoot.IsVisible = true;
            header.Text = title;
        }

        string? subtitle = result.GetValueForOption(subtitleOption);
        if (subtitle != null)
        {
            subheaderRoot.IsVisible = true;
            subheader.Text = subtitle;
        }

        string? icon = result.GetValueForOption(iconOption);
        if (icon != null && Enum.TryParse(icon, out Symbol iconSymbol))
        {
            subheaderRoot.IsVisible = true;
            iconHost.IsVisible = true;
            iconSourceElement.IconSource = new FluentIcons.FluentAvalonia.SymbolIconSource
            {
                Symbol = iconSymbol,
            };
        }

        string? content = result.GetValueForOption(contentOption);
        if (content != null)
        {
            contentPresenter.Content = content;
        }

        if (result.GetValueForOption(progressOption))
        {
            progress.IsVisible = true;
            progress.IsIndeterminate = true;
        }

        _closable = result.GetValueForOption(closableOption);
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        e.Cancel = !_closable;
    }
}
