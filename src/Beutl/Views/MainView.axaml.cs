using System.Collections.ObjectModel;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;

using Beutl.Configuration;
using Beutl.Logging;
using Beutl.Services;
using Beutl.Utilities;
using Beutl.ViewModels;
using Beutl.Views.Dialogs;

using DynamicData;
using DynamicData.Binding;

using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Windowing;

using Microsoft.Extensions.Logging;

using Reactive.Bindings.Extensions;

using ReactiveUI;

namespace Beutl.Views;

public sealed partial class MainView : UserControl
{
    private readonly ILogger<MainView> _logger = Log.CreateLogger<MainView>();
    private readonly CompositeDisposable _disposables = [];

    public MainView()
    {
        InitializeComponent();

        // NavigationViewの設定
        Navi.MenuItemsSource = _navigationItems;
        Navi.ItemInvoked += NavigationView_ItemInvoked;

        recentFiles.ItemsSource = _rawRecentFileItems;
        recentProjects.ItemsSource = _rawRecentProjItems;
        if (OperatingSystem.IsMacOS())
        {
            WindowIcon.IsVisible = false;
            MenuBar.IsVisible = false;
            Titlebar.Height = 30;
            NotificationPanel.Margin = new(0, 30 + 8, 8, 0);
            OpenNotificationsButton.Margin = default;
            OpenNotificationsButton.Padding = default;
            NotificationInfoBadge.Height = 16;
            NotificationInfoBadge.Width = 16;

            Titlebar.ColumnDefinitions[^3].Width = GridLength.Star;
            Titlebar.ColumnDefinitions[^2].Width = GridLength.Auto;
            Titlebar.ColumnDefinitions[^1].Width = GridLength.Auto;
        }
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

    private void OnParentWindowOpened(object? sender, EventArgs e)
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
            InitExtMenuItems(viewModel);
        }

        ShowTelemetryDialog();

        _logger.LogInformation("Window opened.");
    }

    private static async void ShowTelemetryDialog()
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

    // 拡張機能を読み込んだ後に呼び出す
    private void InitExtMenuItems(MainViewModel viewModel)
    {
        // ToolTabExtensionをメニューに表示する
        static MenuItem CreateToolTabMenuItem(ToolTabExtension item)
        {
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

            return menuItem;
        }

        viewModel.ToolTabExtensions.ToObservableChangeSet()
            .ObserveOnUIDispatcher()
            .Filter(i => i.Header != null)
            .Cast(CreateToolTabMenuItem)
            .Bind(out ReadOnlyObservableCollection<MenuItem>? list1)
            .Subscribe()
            .DisposeWith(_disposables);

        toolTabMenuItem.ItemsSource = list1;

        // EditorExtensionをメニューに表示する
        static MenuItem CreateEditorMenuItem(EditorExtension item)
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

            return menuItem;
        }

        viewModel.EditorExtensions.ToObservableChangeSet()
            .ObserveOnUIDispatcher()
            .Cast(CreateEditorMenuItem)
            .Bind(out ReadOnlyObservableCollection<MenuItem>? list2)
            .Subscribe()
            .DisposeWith(_disposables);

        editorTabMenuItem.ItemsSource = list2;

        viewMenuItem.SubmenuOpened += (s, e) =>
        {
            EditorTabItem? selectedTab = EditorService.Current.SelectedTabItem.Value;
            if (selectedTab != null)
            {
                foreach (MenuItem item in list2.OfType<MenuItem>())
                {
                    if (item.DataContext is EditorExtension editorExtension)
                    {
                        item.IsVisible = editorExtension.IsSupported(selectedTab.FilePath.Value);
                    }
                }
            }
        };
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

    [Conditional("DEBUG")]
    private void MonitorKeyModifier_Click(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is Window owner)
        {
            new KeyModifierMonitor().Show(owner);
        }
    }

    [Conditional("DEBUG")]
    private void ThrowUnhandledException_Click(object? sender, RoutedEventArgs e)
    {
        throw new Exception("An unhandled exception occurred.");
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
