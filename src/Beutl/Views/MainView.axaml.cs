using Avalonia;
using Avalonia.Controls;
using Beutl.Configuration;
using Beutl.Logging;
using Beutl.Services.PrimitiveImpls;
using Beutl.ViewModels;
using Beutl.Views.Dialogs;
using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Windowing;
using Microsoft.Extensions.Logging;
using NuGet.Versioning;

namespace Beutl.Views;

public sealed partial class MainView : UserControl
{
    private readonly ILogger<MainView> _logger = Log.CreateLogger<MainView>();
    private readonly CompositeDisposable _disposables = [];

    public MainView()
    {
        InitializeComponent();

        recentFiles.ItemsSource = _rawRecentFileItems;
        recentProjects.ItemsSource = _rawRecentProjItems;
        SetupMacOSBehavior();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        _disposables.Clear();
        if (DataContext is MainViewModel viewModel)
        {
            InitializeCommands(viewModel);
            InitializeRecentItems(viewModel);
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        if (e.Root is TopLevel b)
        {
            b.Opened += OnParentWindowOpened;
        }
    }

    private async void OnParentWindowOpened(object? sender, EventArgs e)
    {
        var topLevel = (TopLevel)sender!;
        topLevel.Opened -= OnParentWindowOpened;
        var cm = App.GetContextCommandManager();
        cm?.Attach(this, MainViewExtension.Instance);

        if (sender is AppWindow cw)
        {
            AppWindowTitleBar titleBar = cw.TitleBar;
            if (titleBar != null)
            {
                titleBar.ExtendsContentIntoTitleBar = true;

                Titlebar.Margin = new Thickness(0, 0, titleBar.LeftInset, 0);
                AppWindow.SetAllowInteractionInTitleBar(MenuBar, true);
                AppWindow.SetAllowInteractionInTitleBar(OpenNotificationsButton, true);
                NotificationPanel.Margin = new(0, titleBar.Height + 8, 8, 0);
            }
        }

        if (DataContext is MainViewModel viewModel)
        {
            InitExtMenuItems(viewModel);
        }

        await ShowTelemetryDialog();
        await CheckDifferentVersion();

        _logger.LogInformation("Window opened.");
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
                    var dialog = new ContentDialog
                    {
                        Title = Message.CheckDifferentVersion_Title,
                        Content = Message.CheckDifferentVersion_Content,
                        PrimaryButtonText = Strings.Close
                    };
                    await dialog.ShowAsync();
                }
            }
        }
    }

    private static async Task ShowTelemetryDialog()
    {
        TelemetryConfig tconfig = GlobalConfiguration.Instance.TelemetryConfig;
        if (!(tconfig.Beutl_Api_Client.HasValue
              && tconfig.Beutl_Application.HasValue
              && tconfig.Beutl_PackageManagement.HasValue
              && tconfig.Beutl_Logging.HasValue))
        {
            var dialog = new TelemetryDialog();

            bool result = await dialog.ShowAsync() == ContentDialogResult.Primary;
            tconfig.Beutl_Api_Client = result;
            tconfig.Beutl_Application = result;
            tconfig.Beutl_PackageManagement = result;
            tconfig.Beutl_Logging = result;
        }
    }
}
