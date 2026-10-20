using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Beutl.AgentToolkit.Installation;
using Beutl.Configuration;
using Beutl.Language;
using Beutl.Logging;
using Beutl.Pages;
using Beutl.Services;
using Beutl.Services.PrimitiveImpls;
using Beutl.Services.Tutorials;
using Beutl.Services.WindowCapture;
using Beutl.Threading;
using Beutl.ViewModels;
using Beutl.ViewModels.SettingsPages;
using Beutl.Views.Tutorial;
using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Windowing;
using Microsoft.Extensions.Logging;
using NuGet.Versioning;

namespace Beutl.Views;

public sealed partial class MainView : UserControl
{
    private readonly ILogger<MainView> _logger = Log.CreateLogger<MainView>();
    private readonly CompositeDisposable _disposables = [];
    private readonly ToolWindowLauncher _toolWindowLauncher = new();
    private WindowCaptureSession? _captureSession;
    private readonly SingleFlightAsyncOperation _captureStop = new();

    public MainView()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, TabSwitcherOverlay.HandleKeyDown, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, TabSwitcherOverlay.HandleKeyUp, RoutingStrategies.Tunnel, handledEventsToo: true);

        ((Avalonia.Controls.Primitives.PopupFlyoutBase)OpenNotificationsButton.Flyout!).Opening += OnNotificationsFlyoutOpening;
        if (OperatingSystem.IsMacOS())
        {
            WindowIcon.IsVisible = false;
            MenuBar.IsVisible = false;
            Titlebar.Height = 40;
            NotificationPanel.Margin = new(0, 40 + 8, 8, 0);

            TitleBreadcrumbBar.Margin = new(80, 0, 8, 0);

            Titlebar.PointerPressed += (s, e) =>
            {
                if (e.Source is Visual source
                    && source.FindAncestorOfType<TitleBarBranchView>(
                        includeSelf: true) is not null)
                {
                    return;
                }

                if (TopLevel.GetTopLevel(this) is Window window && window.WindowState != WindowState.FullScreen)
                {
                    if (e.ClickCount == 2)
                    {
                        window.WindowState = window.WindowState == WindowState.Maximized
                            ? WindowState.Normal
                            : WindowState.Maximized;
                    }
                    else
                    {
                        window.BeginMoveDrag(e);
                    }
                }
            };
        }
    }

    // With no notification there is nothing to show, so the bell opens no flyout.
    private void OnNotificationsFlyoutOpening(object? sender, EventArgs e)
    {
        if (HiddenNotificationPanel.Children.Count == 0 && e is System.ComponentModel.CancelEventArgs cancel)
            cancel.Cancel = true;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        _disposables.Clear();
        if (DataContext is MainViewModel viewModel)
        {
            InitializeCommands(viewModel);
            MainMenuBuilder.AddMenus(MenuBar, viewModel.MenuItems, _disposables);
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        if (TopLevel.GetTopLevel(this) is { } b)
        {
            b.Opened += OnParentWindowOpened;
        }
    }

    private async void OnParentWindowOpened(object? sender, EventArgs e)
    {
        var topLevel = (TopLevel)sender!;
        topLevel.Opened -= OnParentWindowOpened;
        var cm = (DataContext as MainViewModel)?.ContextCommandManager;
        cm?.Attach(this, MainViewExtension.Instance);

        if (sender is FAAppWindow cw)
        {
            FAAppWindowTitleBar titleBar = cw.TitleBar;
            titleBar.ExtendsContentIntoTitleBar = true;

            // Avalonia 12 draws three 46 DIP caption buttons; reserve their width plus spacing.
            Titlebar.Margin = new Thickness(0, 0, OperatingSystem.IsWindows() ? 140 : 0, 0);
            WindowDecorationProperties.SetElementRole(Titlebar, WindowDecorationsElementRole.TitleBar);
            NotificationPanel.Margin = new(0, titleBar.Height + 8, 8, 0);
        }

        StartupNotificationService.ShowTelemetryConsent(GlobalConfiguration.Instance.TelemetryConfig);
        await CheckDifferentVersion();
        await CheckAgentToolkitUpdateAsync();

        _logger.LogInformation("Window opened.");
    }

    private async Task CheckAgentToolkitUpdateAsync()
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        try
        {
            (AgentToolkitInstallManifest? manifest, IReadOnlyList<AgentToolkitAsset> assets) = await Task.Run(() =>
                (AgentToolkitInstallManifestStore.Load(AgentToolkitInstallManifestStore.GetDefaultPath()),
                    (IReadOnlyList<AgentToolkitAsset>)BundledAgentToolkitAssets.Load()));

            if (!AgentToolkitInstallManifestStore.IsUpdateAvailable(manifest, assets))
            {
                return;
            }

            // An older MCP layout means the agent config still carries the separate live entry and
            // its token; say so instead of announcing newer skills.
            bool mcpChanged = AgentToolkitInstallManifestStore.IsMcpLayoutOutdated(manifest);
            NotificationService.ShowInformation(
                mcpChanged ? SettingsStrings.AiAgents_McpChanged_Title : SettingsStrings.AiAgents_UpdateAvailable_Title,
                mcpChanged ? SettingsStrings.AiAgents_McpChanged_Content : SettingsStrings.AiAgents_UpdateAvailable_Content,
                expiration: TimeSpan.FromSeconds(30),
                actions:
                [
                    new(SettingsStrings.AiAgents_Reinstall,
                        () => _ = ReinstallAgentToolkitAsync(viewModel))
                ]);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Agent toolkit update check failed.");
        }
    }

    private async Task ReinstallAgentToolkitAsync(MainViewModel viewModel)
    {
        try
        {
            using var settingsViewModel = new AiAgentSettingsPageViewModel();
            await settingsViewModel.InstallAsync();
            // The entry to add by hand, when the agent's MCP configuration has comments Beutl keeps.
            string message = settingsViewModel.ManualMcpSnippet.Value is { Length: > 0 } snippet
                ? settingsViewModel.Status.Value + Environment.NewLine + snippet
                : settingsViewModel.Status.Value;
            NotificationService.ShowInformation(SettingsStrings.AiAgents, message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Agent toolkit reinstall failed.");
            NotificationService.ShowError(SettingsStrings.AiAgents, ex.Message);
        }
    }

    private static async Task CheckDifferentVersion()
    {
        if (NuGetVersion.TryParse(GlobalConfiguration.Instance.LastStartedVersion, out var lastStartedVersion) &&
            NuGetVersion.TryParse(BeutlApplication.Version, out var currentVersion))
        {
            if (lastStartedVersion.IsPrerelease || currentVersion.IsPrerelease)
            {
                if (lastStartedVersion < currentVersion)
                {
                    var dialog = new FAContentDialog
                    {
                        Title = MessageStrings.CheckDifferentVersion_Title,
                        Content = MessageStrings.CheckDifferentVersion_Content,
                        PrimaryButtonText = Strings.Close
                    };
                    await dialog.ShowAsync();
                }
            }
        }
    }

    private async void OpenToolWindow(ToolWindowExtension extension)
    {
        await _toolWindowLauncher.OpenAsync(extension, () => TopLevel.GetTopLevel(this) as Window);
    }

    private async void GoToInformationPage() => await GoToInformationPageAsync();

    internal Task GoToInformationPageAsync()
    {
        return ShowSettingsDialogAsync(static dialogViewModel => dialogViewModel.GoToSettingsPage());
    }

    private async Task ShowSettingsDialogAsync(Action<SettingsDialogViewModel> navigate)
    {
        if (DataContext is MainViewModel viewModel && TopLevel.GetTopLevel(this) is Window window)
        {
            using var dialogViewModel = viewModel.CreateSettingsDialog();
            var dialog = new SettingsDialog { DataContext = dialogViewModel };
            navigate(dialogViewModel);
            await dialog.ShowDialog(window);
        }
    }

    private void OpenFeedbackClick(object? sender, RoutedEventArgs e)
    {
        string url = $"https://beutl.beditor.net/feedback?traceId={Telemetry.Instance._sessionId}";
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    private async void OpenSettingsDialog()
    {
        await ShowSettingsDialogAsync(static dialogViewModel => dialogViewModel.GoToAccountSettingsPage());
    }

    private async void OpenTutorialsDialog() => await ShowTutorialsDialogAsync();

    internal async Task ShowTutorialsDialogAsync()
    {
        var dialog = new TutorialListDialog();
        await dialog.ShowAsync();
    }
}
