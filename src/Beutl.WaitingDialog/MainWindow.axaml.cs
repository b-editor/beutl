using System.CommandLine;
using System.CommandLine.Parsing;
using System.Diagnostics;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Threading;

using FluentAvalonia.Styling;
using FluentAvalonia.UI.Windowing;

using Icon = FluentIcons.Common.Icon;

namespace Beutl.WaitingDialog;

public partial class MainWindow : FAAppWindow
{
    private bool _closable;
    private Process? _parentProcess;

    public MainWindow()
    {
        InitializeComponent();
        ShowAsDialog = true;
        Topmost = true;
        var titleOption = new Option<string?>("--title");
        var subtitleOption = new Option<string?>("--subtitle");
        var iconOption = new Option<string?>("--icon");
        var contentOption = new Option<string?>("--content");
        var progressOption = new Option<bool>("--progress");
        var closableOption = new Option<bool>("--closable");
        var themeOption = new Option<string?>("--theme");
        var parentOption = new Option<int?>("--parent");
        var command = new RootCommand()
        {
            titleOption,
            subtitleOption,
            iconOption,
            contentOption,
            progressOption,
            closableOption,
            parentOption,
            themeOption
        };

        string[] args = ((ClassicDesktopStyleApplicationLifetime)Application.Current!.ApplicationLifetime!).Args!;
        ParseResult result = command.Parse(args);

        string? title = result.GetValue(titleOption);
        if (title != null)
        {
            headerRoot.IsVisible = true;
            header.Text = title;
        }

        string? subtitle = result.GetValue(subtitleOption);
        if (subtitle != null)
        {
            subheaderRoot.IsVisible = true;
            subheader.Text = subtitle;
        }

        string? icon = result.GetValue(iconOption);
        if (icon != null && Enum.TryParse(icon, out Icon iconSymbol))
        {
            subheaderRoot.IsVisible = true;
            iconHost.IsVisible = true;
            iconSourceElement.IconSource = new FluentIcons.Avalonia.Fluent.FluentIconSource
            {
                Icon = iconSymbol,
            };
        }

        string? content = result.GetValue(contentOption);
        if (content != null)
        {
            contentPresenter.Content = content;
        }

        string? theme = result.GetValue(themeOption);
        if (theme != null)
        {
            ApplyTheme(theme);
        }

        if (result.GetValue(progressOption))
        {
            progress.IsVisible = true;
            progress.IsIndeterminate = true;
        }

        _closable = result.GetValue(closableOption);

        int? parent = result.GetValue(parentOption);
        if (parent.HasValue)
        {
            WatchParentProcess(parent.Value);
        }
    }

    private static void ApplyTheme(string theme)
    {
        Application.Current!.RequestedThemeVariant = theme switch
        {
            "light" => ThemeVariant.Light,
            "dark" => ThemeVariant.Dark,
            "highcontrast" => FluentAvaloniaTheme.HighContrastTheme,
            _ => ThemeVariant.Default,
        };
    }

    private void WatchParentProcess(int processId)
    {
        try
        {
            _parentProcess = Process.GetProcessById(processId);
            _parentProcess.EnableRaisingEvents = true;
            _parentProcess.Exited += OnParentExited;
        }
        catch
        {
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
