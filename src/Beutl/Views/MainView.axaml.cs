using System.Collections;

using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;

using Beutl.Api.Services;
using Beutl.Configuration;
using Beutl.Services;
using Beutl.Utilities;
using Beutl.ViewModels;
using Beutl.Views.Dialogs;

using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Windowing;

using Reactive.Bindings;

using Serilog;

namespace Beutl.Views;

public sealed partial class MainView : UserControl
{
    private readonly ILogger _logger = Log.ForContext<MainView>();
    private readonly CompositeDisposable _disposables = new();

    public MainView()
    {
        InitializeComponent();

        // NavigationViewの設定
        Navi.MenuItemsSource = _navigationItems;
        Navi.ItemInvoked += NavigationView_ItemInvoked;

        recentFiles.ItemsSource = _rawRecentFileItems;
        recentProjects.ItemsSource = _rawRecentProjItems;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (DataContext is MainViewModel viewModel)
        {
            // KeyBindingsは変更してはならない。
            foreach (KeyBinding binding in viewModel.KeyBindings)
            {
                if (e.Handled)
                    break;
                binding.TryHandle(e);
            }
        }
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        _disposables.Clear();
        if (DataContext is MainViewModel viewModel)
        {
            InitializePages(viewModel);
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
            await App.WaitLoadingExtensions();

            InitExtMenuItems();
        }

        _logger.Information("WindowOpened");

        ShowTelemetryDialog();

        Telemetry.WindowOpened();
    }

    private static async void ShowTelemetryDialog()
    {
        TelemetryConfig tconfig = GlobalConfiguration.Instance.TelemetryConfig;
        if (!(tconfig.Beutl_Api_Client.HasValue
            && tconfig.Beutl_Application.HasValue
            && tconfig.Beutl_ViewTracking.HasValue
            && tconfig.Beutl_PackageManagement.HasValue
            && tconfig.Beutl_All_Errors.HasValue))
        {
            var dialog = new TelemetryDialog();

            bool result = await dialog.ShowAsync() == ContentDialogResult.Primary;
            tconfig.Beutl_Api_Client = result;
            tconfig.Beutl_Application = result;
            tconfig.Beutl_PackageManagement = result;
            tconfig.Beutl_ViewTracking = result;
            tconfig.Beutl_All_Errors = result;
        }
    }

    // 拡張機能を読み込んだ後に呼び出す
    private void InitExtMenuItems()
    {
        if (toolTabMenuItem.Items is not IList items1)
        {
            items1 = new AvaloniaList<object>();
            toolTabMenuItem.ItemsSource = items1;
        }

        // Todo: Extensionの実行時アンロードの実現時、
        //       ForEachItemメソッドを使うかeventにする
        foreach (ToolTabExtension item in ExtensionProvider.Current.GetExtensions<ToolTabExtension>())
        {
            if (item.Header == null)
                continue;

            var menuItem = new MenuItem()
            {
                Header = item.Header,
                DataContext = item
            };

            menuItem.Click += (s, e) =>
            {
                if (EditorService.Current.SelectedTabItem.Value?.Context.Value is IEditorContext editorContext
                    && s is MenuItem { DataContext: ToolTabExtension ext }
                    && ext.TryCreateContext(editorContext, out IToolContext? toolContext))
                {
                    bool result = editorContext.OpenToolTab(toolContext);
                    if (!result)
                    {
                        toolContext.Dispose();
                    }
                }
            };

            items1.Add(menuItem);
        }

        if (editorTabMenuItem.Items is not IList items2)
        {
            items2 = new AvaloniaList<object>();
            editorTabMenuItem.ItemsSource = items2;
        }

        viewMenuItem.SubmenuOpened += (s, e) =>
        {
            EditorTabItem? selectedTab = EditorService.Current.SelectedTabItem.Value;
            if (selectedTab != null)
            {
                foreach (MenuItem item in items2.OfType<MenuItem>())
                {
                    if (item.DataContext is EditorExtension editorExtension)
                    {
                        item.IsVisible = editorExtension.IsSupported(selectedTab.FilePath.Value);
                    }
                }
            }
        };

        foreach (EditorExtension item in ExtensionProvider.Current.GetExtensions<EditorExtension>())
        {
            var menuItem = new MenuItem()
            {
                Header = item.DisplayName,
                DataContext = item,
                IsVisible = false,
                Icon = item.GetIcon()
            };

            menuItem.Click += async (s, e) =>
            {
                EditorTabItem? selectedTab = EditorService.Current.SelectedTabItem.Value;
                if (s is MenuItem { DataContext: EditorExtension editorExtension } menuItem
                    && selectedTab != null)
                {
                    IKnownEditorCommands? commands = selectedTab.Commands.Value;
                    if (commands != null)
                    {
                        await commands.OnSave();
                    }

                    string file = selectedTab.FilePath.Value;
                    if (editorExtension.TryCreateContext(file, out IEditorContext? context))
                    {
                        selectedTab.Context.Value.Dispose();
                        selectedTab.Context.Value = context;
                    }
                    else
                    {
                        NotificationService.ShowInformation(
                            title: Message.ContextNotCreated,
                            message: string.Format(
                                format: Message.CouldNotOpenFollowingFileWithExtension,
                                arg0: editorExtension.DisplayName,
                                arg1: selectedTab.FileName.Value));
                    }
                }
            };

            items2.Add(menuItem);
        }
    }

    [Conditional("DEBUG")]
    private void GC_Collect_Click(object? sender, RoutedEventArgs e)
    {
        DateTime dateTime = DateTime.UtcNow;
        long totalBytes = GC.GetTotalMemory(false);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        TimeSpan elapsed = DateTime.UtcNow - dateTime;

        long deltaBytes = GC.GetTotalMemory(false) - totalBytes;
        string str = StringFormats.ToHumanReadableSize(Math.Abs(deltaBytes));
        str = (deltaBytes >= 0 ? "+" : "-") + str;

        NotificationService.ShowInformation(
            "結果",
            $"""
                    経過時間: {elapsed.TotalMilliseconds}ms
                    差: {str}
                    """);
    }

    private void GoToInfomationPage(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.SelectedPage.Value = viewModel.SettingsPage;
            (viewModel.SettingsPage.Context as SettingsPageViewModel)?.GoToSettingsPage();
        }
    }

    private void OpenNotificationsClick(object? sender, RoutedEventArgs e)
    {
        if (HiddenNotificationPanel.Children.Count > 0
            && sender is Button btn)
        {
            btn.Flyout?.ShowAt(btn);
        }
    }
}
