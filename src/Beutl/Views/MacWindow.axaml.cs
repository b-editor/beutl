using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Beutl.Helpers;
using Beutl.Language;
using Beutl.Services;
using Beutl.ViewModels;
using DynamicData;
using DynamicData.Binding;
using Reactive.Bindings.Extensions;

namespace Beutl.Views;

public sealed partial class MacWindow : Window
{
    private readonly ToolWindowLauncher _toolWindowLauncher = new();
    private readonly CompositeDisposable _menuDisposables = [];
    private readonly MainWindowCloseGuard _closeGuard;
    private MacOSTitleBar? _titleBar;

    public MacWindow()
    {
        if (OperatingSystem.IsMacOS())
        {
            ExtendClientAreaToDecorationsHint = true;
            ExtendClientAreaTitleBarHeightHint = 40;
        }

        InitializeComponent();
        _closeGuard = new MainWindowCloseGuard(this, mainView);
        WindowPlacement.Restore(this);
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        _titleBar ??= MacOSTitleBar.TryAttach(this);
        WindowPlacement.FitToWorkingArea(this);

        mainView.Focus();

        if (DataContext is MainViewModel viewModel)
        {
            InitExtMenuItems(viewModel);
        }
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        _menuDisposables.Clear();
        if (DataContext is MainViewModel viewModel)
            MainMenuBuilder.AddMenus(NativeMenu.GetMenu(this)!, viewModel.MenuBar.MenuItems, _menuDisposables);
    }

    // Resolved by header so a menu edit cannot shift these.
    internal static NativeMenuItem? FindMenuItem(NativeMenu? menu, string header)
    {
        return menu?.Items
            .OfType<NativeMenuItem>()
            .FirstOrDefault(item => string.Equals(item.Header, header, StringComparison.Ordinal));
    }

    internal void InitExtMenuItems(MainViewModel viewModel)
    {
        NativeMenu rootMenu = NativeMenu.GetMenu(this)!;
        NativeMenuItem? viewMenuItem = FindMenuItem(rootMenu, Strings.View);
        NativeMenu? editorTabMenu = FindMenuItem(viewMenuItem?.Menu, Strings.Editors)?.Menu;
        NativeMenu? toolTabMenu = FindMenuItem(viewMenuItem?.Menu, Strings.Tools)?.Menu;
        NativeMenu? toolWindowMenu = FindMenuItem(rootMenu, Strings.Tools)?.Menu;
        NativeMenu? dockLayoutPresetMenu = FindMenuItem(viewMenuItem?.Menu, Strings.ApplyDockLayout)?.Menu;

        if (viewMenuItem == null || editorTabMenu == null || toolTabMenu == null || toolWindowMenu == null) return;

        if (dockLayoutPresetMenu != null)
        {
            InitDockLayoutPresetMenu(viewModel, dockLayoutPresetMenu);
        }

        NativeMenuItem CreateToolTabMenuItem(ToolTabExtension item)
        {
            var menuItem = new NativeMenuItem() { Header = item.Header, CommandParameter = item };

            menuItem.Click += (s, e) =>
            {
                if (s is NativeMenuItem { CommandParameter: ToolTabExtension ext })
                {
                    ExtensionMenuActions.OpenToolTab(viewModel, ext);
                }
            };

            return menuItem;
        }

        viewModel.ToolTabExtensions.ToObservableChangeSet()
            .ObserveOnUIDispatcher()
            .Filter(i => i.Header != null)
            .Cast(CreateToolTabMenuItem)
            .Bind(out ReadOnlyObservableCollection<NativeMenuItem>? list1)
            .Subscribe();

        list1.ForEachItem<NativeMenuItem, ReadOnlyObservableCollection<NativeMenuItem>>(
            toolTabMenu.Items.Insert,
            (i, _) => toolTabMenu.Items.RemoveAt(i),
            toolTabMenu.Items.Clear);

        NativeMenuItem CreateEditorMenuItem(EditorExtension item)
        {
            var menuItem = new NativeMenuItem()
            {
                Header = item.DisplayName,
                CommandParameter = item,
                // Todo: Avalonia 11.1.0から
                // IsVisible = false
                IsEnabled = false
            };

            menuItem.Click += async (s, e) =>
            {
                EditorTabItem? selectedTab = viewModel.EditorService.SelectedTabItem.Value;
                if (s is NativeMenuItem { CommandParameter: EditorExtension editorExtension }
                    && selectedTab != null)
                {
                    await ExtensionMenuActions.SwitchEditorAsync(viewModel, selectedTab, editorExtension);
                }
            };

            return menuItem;
        }

        viewModel.EditorExtensions.ToObservableChangeSet()
            .ObserveOnUIDispatcher()
            .Cast(CreateEditorMenuItem)
            .Bind(out ReadOnlyObservableCollection<NativeMenuItem>? list2)
            .Subscribe();

        list2.ForEachItem<NativeMenuItem, ReadOnlyObservableCollection<NativeMenuItem>>(
            editorTabMenu.Items.Insert,
            (i, _) => editorTabMenu.Items.RemoveAt(i),
            editorTabMenu.Items.Clear);

        viewMenuItem.Menu!.Opening += (s, e) =>
        {
            EditorTabItem? selectedTab = viewModel.EditorService.SelectedTabItem.Value;
            if (selectedTab != null)
            {
                foreach (NativeMenuItem item in list2.OfType<NativeMenuItem>())
                {
                    if (item.CommandParameter is EditorExtension editorExtension)
                    {
                        // Todo: Avalonia 11.1.0から
                        // item.IsVisible = editorExtension.IsSupported(selectedTab.FilePath.Value);
                        item.IsEnabled = editorExtension.IsSupported(selectedTab.FilePath.Value);
                    }
                }
            }
        };

        NativeMenuItem CreateToolWindowMenuItem(ToolWindowExtension item)
        {
            var menuItem = new NativeMenuItem() { Header = item.DisplayName, CommandParameter = item };

            menuItem.Click += async (s, e) =>
            {
                if (s is NativeMenuItem { CommandParameter: ToolWindowExtension ext })
                {
                    await OpenToolWindowAsync(ext);
                }
            };

            return menuItem;
        }

        var toolWindowSource = viewModel.ToolWindowExtensions.ToObservableChangeSet()
            .ObserveOnUIDispatcher()
            .Transform<ToolWindowExtension, NativeMenuItem>(CreateToolWindowMenuItem);

        toolWindowSource
            .Bind(out ReadOnlyObservableCollection<NativeMenuItem>? toolWindowMenuItems)
            .Subscribe();

        toolWindowMenuItems.ForEachItem<NativeMenuItem, ReadOnlyObservableCollection<NativeMenuItem>>(
            toolWindowMenu.Items.Insert,
            (i, _) => toolWindowMenu.Items.RemoveAt(i),
            toolWindowMenu.Items.Clear);
    }

    private static void InitDockLayoutPresetMenu(MainViewModel viewModel, NativeMenu menu)
    {
        // Renaming mutates Name.Value in place without a collection change, so each item's header
        // follows its name observable. The subscriptions are keyed by menu item so removal and
        // clear can dispose them.
        var nameSubscriptions = new Dictionary<NativeMenuItem, IDisposable>();

        void AddItem(int index, DockLayoutPresetItem item)
        {
            var menuItem = new NativeMenuItem
            {
                Header = item.Name.Value,
                Command = viewModel.MenuBar.ApplyDockLayout,
                CommandParameter = item
            };

            nameSubscriptions[menuItem] = item.Name.Subscribe(name => menuItem.Header = name);
            menu.Items.Insert(index, menuItem);
        }

        void RemoveAt(int index)
        {
            if (menu.Items[index] is NativeMenuItem menuItem
                && nameSubscriptions.Remove(menuItem, out IDisposable? subscription))
            {
                subscription.Dispose();
            }

            menu.Items.RemoveAt(index);
        }

        void Clear()
        {
            foreach (IDisposable subscription in nameSubscriptions.Values)
            {
                subscription.Dispose();
            }

            nameSubscriptions.Clear();
            menu.Items.Clear();
        }

        viewModel.MenuBar.DockLayoutPresets.ForEachItem(
            AddItem,
            (i, _) => RemoveAt(i),
            Clear);
    }

    private Task OpenToolWindowAsync(ToolWindowExtension extension)
    {
        return _toolWindowLauncher.OpenAsync(extension, () => this);
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (_closeGuard.CancelClosing(e))
            return;

        base.OnClosing(e);
        WindowPlacement.Save(this);
    }

    private async void OpenTutorialsDialog(object? sender, EventArgs e) => await mainView.ShowTutorialsDialogAsync();

    private async void GoToInformationPage(object? sender, EventArgs e) => await mainView.GoToInformationPageAsync();

    private void GC_Collect_Click(object? sender, EventArgs e) => mainView.RunGcCollect();

    private void MonitorKeyModifier_Click(object? sender, EventArgs e) => mainView.OpenKeyModifierMonitor();

    private void ThrowUnhandledException_Click(object? sender, EventArgs e) => mainView.ThrowDebugException();

    private async void StartWindowCapture_Click(object? sender, EventArgs e) => await mainView.StartWindowCaptureAsync();

    private async void StopWindowCapture_Click(object? sender, EventArgs e) => await mainView.StopWindowCaptureAsync();
}
