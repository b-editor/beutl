using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Beutl.Pages;
using Beutl.Services;
using Beutl.Utilities;
using Beutl.ViewModels;
using DynamicData;
using DynamicData.Binding;
using Reactive.Bindings.Extensions;

namespace Beutl.Views;

public sealed partial class MainView
{
    private void SetupMacOSBehavior()
    {
        if (!OperatingSystem.IsMacOS()) return;

        WindowIcon.IsVisible = false;
        MenuBar.IsVisible = false;
        Titlebar.Height = 40;
        NotificationPanel.Margin = new(0, 40 + 8, 8, 0);

        TitleBreadcrumbBar.Margin = new(80, 0, 8, 0);
        Titlebar.ColumnDefinitions[^2].Width = GridLength.Star;
        Titlebar.ColumnDefinitions[^1].Width = GridLength.Auto;

        Titlebar.PointerPressed += OnTitlebarPointerPressed;
    }

    private void OnTitlebarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not Window window) return;
        if (window.WindowState == WindowState.FullScreen) return;

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

    // 拡張機能を読み込んだ後に呼び出す
    private void InitExtMenuItems(MainViewModel viewModel)
    {
        // ToolTabExtensionをメニューに表示する

        viewModel.ToolTabExtensions.ToObservableChangeSet()
            .ObserveOnUIDispatcher()
            .Filter(i => i.Header != null)
            .Cast(CreateToolTabMenuItem)
            .Bind(out ReadOnlyObservableCollection<MenuItem>? list1)
            .Subscribe()
            .DisposeWith(_disposables);

        toolTabMenuItem.ItemsSource = list1;

        // EditorExtensionをメニューに表示する

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

        // PageExtension(Dialog)をメニューに表示する

        viewModel.PageExtensions.ToObservableChangeSet()
            .ObserveOnUIDispatcher()
            .Cast(CreateToolWindowMenuItem)
            .Bind(out ReadOnlyObservableCollection<MenuItem>? list3)
            .Subscribe()
            .DisposeWith(_disposables);

        toolWindowMenuItem.ItemsSource = list3;
    }

    private MenuItem CreateToolWindowMenuItem(PageExtension item)
    {
        var menuItem = new MenuItem
        {
            Header = item.DisplayName,
            DataContext = item,
            Icon = item.GetRegularIcon()
        };

        menuItem.Click += async (s, e) =>
        {
            try
            {
                if (s is not MenuItem { DataContext: PageExtension pageExtension })
                    return;

                if (TopLevel.GetTopLevel(this) is not Window topLevel)
                    return;

                var controlOrDialog = pageExtension.CreateControl();
                var dataContext = pageExtension.CreateContext();
                controlOrDialog.DataContext = dataContext;
                if (controlOrDialog is Window dialog)
                {
                    await dialog.ShowDialog(topLevel);
                }
                else
                {
                    var window = new Window { Content = controlOrDialog, Title = dataContext.Header };
                    await window.ShowDialog(topLevel);
                }
            }
            catch (Exception ex)
            {
                await ex.Handle();
            }
        };

        return menuItem;
    }

    private static MenuItem CreateEditorMenuItem(EditorExtension item)
    {
        var menuItem = new MenuItem
        {
            Header = item.DisplayName,
            DataContext = item,
            IsVisible = false,
            Icon = item.GetIcon()
        };

        menuItem.Click += async (s, e) =>
        {
            EditorTabItem? selectedTab = EditorService.Current.SelectedTabItem.Value;
            if (selectedTab == null) return;
            if (s is not MenuItem { DataContext: EditorExtension editorExtension }) return;

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
        };

        return menuItem;
    }

    private static MenuItem CreateToolTabMenuItem(ToolTabExtension item)
    {
        var menuItem = new MenuItem { Header = item.Header, DataContext = item };

        menuItem.Click += (s, e) =>
        {
            if (EditorService.Current.SelectedTabItem.Value?.Context.Value is not { } editorContext) return;
            if (s is not MenuItem { DataContext: ToolTabExtension ext }) return;
            if (!ext.TryCreateContext(editorContext, out IToolContext? toolContext)) return;

            bool result = editorContext.OpenToolTab(toolContext);
            if (!result)
            {
                toolContext.Dispose();
            }
        };

        return menuItem;
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
        if (TopLevel.GetTopLevel(this) is not Window owner) return;

        new KeyModifierMonitor().Show(owner);
    }

    [Conditional("DEBUG")]
    private void ThrowUnhandledException_Click(object? sender, RoutedEventArgs e)
    {
        throw new Exception("An unhandled exception occurred.");
    }

    private async void GoToInformationPage(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel) return;
        if (TopLevel.GetTopLevel(this) is not Window window) return;

        using var dialogViewModel = viewModel.CreateSettingsDialog();
        var dialog = new SettingsDialog { DataContext = dialogViewModel };
        dialogViewModel.GoToSettingsPage();
        await dialog.ShowDialog(window);
    }

    private void OpenNotificationsClick(object? sender, RoutedEventArgs e)
    {
        if (HiddenNotificationPanel.Children.Count > 0
            && sender is Button btn)
        {
            btn.Flyout?.ShowAt(btn);
        }
    }

    private async void OpenSettingsDialog(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel) return;
        if (TopLevel.GetTopLevel(this) is not Window window) return;

        using var dialogViewModel = viewModel.CreateSettingsDialog();
        var dialog = new SettingsDialog { DataContext = dialogViewModel };
        dialogViewModel.GoToAccountSettingsPage();
        await dialog.ShowDialog(window);
    }
}
