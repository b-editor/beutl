using System.CommandLine;
using System.CommandLine.Parsing;
using System.ComponentModel;
using System.Diagnostics;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Threading;

using FluentAvalonia.Styling;
using FluentAvalonia.UI.Windowing;

using Symbol = FluentIcons.Common.Symbol;

namespace Beutl.WaitingDialog;

public partial class MainWindow : AppWindow
{
    private bool _closable;
    private Process? _parentProcess;

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
        var themeOption = new Option<string?>("--theme", () => null);
        var parentProcecss = new Option<int?>("--parent", () => null);
        var command = new RootCommand()
        {
            titleOption,
            subtitleOption,
            iconOption,
            contentOption,
            progressOption,
            closableOption,
            parentProcecss,
            themeOption
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

        string? theme = result.GetValueForOption(themeOption);
        if (theme != null)
        {
            Application.Current.RequestedThemeVariant = theme switch
            {
                "light" => ThemeVariant.Light,
                "dark" => ThemeVariant.Dark,
                "highcontrast" => FluentAvaloniaTheme.HighContrastTheme,
                _ => ThemeVariant.Default,
            };
        }

        if (result.GetValueForOption(progressOption))
        {
            progress.IsVisible = true;
            progress.IsIndeterminate = true;
        }

        _closable = result.GetValueForOption(closableOption);

        int? parent = result.GetValueForOption(parentProcecss);
        if (parent.HasValue)
        {
            try
            {
                _parentProcess = Process.GetProcessById(parent.Value);
                _parentProcess.EnableRaisingEvents = true;
                _parentProcess.Exited += OnParentExited;
            }
            catch
            {
            }
        }
    }

    private void OnParentExited(object? sender, EventArgs e)
    {
        _parentProcess?.Dispose();
        _parentProcess = null;

        Dispatcher.UIThread.Invoke(() =>
        {
            _closable = true;
            Close();
        });
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        e.Cancel = !_closable;
    }
}
