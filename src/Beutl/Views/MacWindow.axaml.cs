using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;

using Beutl.Configuration;
using Beutl.Services;
using Beutl.ViewModels;
using DynamicData;
using DynamicData.Binding;
using FluentAvalonia.UI.Windowing;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.Views;

public sealed partial class MacWindow : Window
{
    public MacWindow()
    {
        InitializeComponent();
        if (OperatingSystem.IsMacOS())
        {
            ExtendClientAreaToDecorationsHint = true;
            //ExtendClientAreaChromeHints = ExtendClientAreaChromeHints.SystemChrome;
            ExtendClientAreaTitleBarHeightHint = 30;
        }

        ViewConfig viewConfig = GlobalConfiguration.Instance.ViewConfig;
        (int X, int Y)? pos = viewConfig.WindowPosition;
        (int Width, int Height)? size = viewConfig.WindowSize;

        if (viewConfig.IsWindowMaximized == true)
        {
            WindowState = WindowState.Maximized;
        }
        else if (pos.HasValue && size.HasValue)
        {
            var rect = new PixelRect(pos.Value.X, pos.Value.Y, size.Value.Width, size.Value.Height);
            SetRect(rect);
        }

#if DEBUG
        this.AttachDevTools();
#endif
    }

    private void SetRect(PixelRect rect)
    {
        Position = rect.Position;
        Width = rect.Width;
        Height = rect.Height;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        Screen? screen = Screens.ScreenFromWindow(this);
        if (screen != null && WindowState != WindowState.Maximized)
        {
            var rect = new PixelRect(Position, PixelSize.FromSize(ClientSize, 1));
            if (!screen.WorkingArea.Contains(rect))
            {
                int width = Math.Min(screen.WorkingArea.Width, rect.Width);
                int height = Math.Min(screen.WorkingArea.Height, rect.Height);
                rect = rect.WithWidth(width).WithHeight(height);

                rect = screen.WorkingArea.CenterRect(rect);
                SetRect(rect);
            }
        }

        mainView.Focus();

        if (DataContext is MainViewModel viewModel)
        {
            InitializeRecentItems(viewModel);
            InitExtMenuItems(viewModel);
        }
    }

    private void InitializeRecentItems(MainViewModel viewModel)
    {
        void AddItem(NativeMenu list, string item, ReactiveCommandSlim<string> command)
        {
            var menuItem = new NativeMenuItem
            {
                Command = command,
                CommandParameter = item,
                Header = item
            };
            list.Add(menuItem);
        }

        void RemoveItem(NativeMenu list, string item)
        {
            for (int i = list.Items.Count - 1; i >= 0; i--)
            {
                if (list.Items[i] is NativeMenuItem menuItem
                    && menuItem.Header is string header
                    && header == item)
                {
                    list.Items.Remove(menuItem);
                }
            }
        }

        NativeMenu? recentFiles = null;
        NativeMenu? recentProj = null;
        try
        {
            var rootMenu = NativeMenu.GetMenu(this)!;
            var fileMenu = (NativeMenuItem)rootMenu.Items[0];
            recentFiles = ((NativeMenuItem)fileMenu.Menu!.Items[^4]).Menu;
            recentProj = ((NativeMenuItem)fileMenu.Menu!.Items[^3]).Menu;
        }
        catch
        {
        }

        if (recentFiles != null && recentProj != null)
        {
            viewModel.MenuBar.RecentFileItems.ForEachItem(
                item => AddItem(recentFiles, item, viewModel.MenuBar.OpenRecentFile),
                item => RemoveItem(recentFiles, item),
                recentFiles.Items.Clear);

            viewModel.MenuBar.RecentProjectItems.ForEachItem(
                item => AddItem(recentProj, item, viewModel.MenuBar.OpenRecentProject),
                item => RemoveItem(recentProj, item),
                recentProj.Items.Clear);
        }
    }

    private void InitExtMenuItems(MainViewModel viewModel)
    {
        NativeMenuItem? viewMenuItem = null;
        NativeMenu? editorTabMenu = null;
        NativeMenu? toolTabMenu = null;
        try
        {
            var rootMenu = NativeMenu.GetMenu(this)!;
            viewMenuItem = (NativeMenuItem)rootMenu.Items[2];
            editorTabMenu = ((NativeMenuItem)viewMenuItem.Menu!.Items[0]).Menu;
            toolTabMenu = ((NativeMenuItem)viewMenuItem.Menu!.Items[1]).Menu;
        }
        catch
        {
        }

        if (viewMenuItem == null || editorTabMenu == null || toolTabMenu == null) return;

        // ToolTabExtensionをメニューに表示する
        static NativeMenuItem CreateToolTabMenuItem(ToolTabExtension item)
        {
            var menuItem = new NativeMenuItem()
            {
                Header = item.Header,
                CommandParameter = item
            };

            menuItem.Click += (s, e) =>
            {
                if (EditorService.Current.SelectedTabItem.Value?.Context.Value is IEditorContext editorContext
                    && s is NativeMenuItem { CommandParameter: ToolTabExtension ext }
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
            .Bind(out ReadOnlyObservableCollection<NativeMenuItem>? list1)
            .Subscribe();

        list1.ForEachItem<NativeMenuItem, ReadOnlyObservableCollection<NativeMenuItem>>(
            toolTabMenu.Items.Insert,
            (i, _) => toolTabMenu.Items.RemoveAt(i),
            toolTabMenu.Items.Clear);

        // EditorExtensionをメニューに表示する
        static NativeMenuItem CreateEditorMenuItem(EditorExtension item)
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
                EditorTabItem? selectedTab = EditorService.Current.SelectedTabItem.Value;
                if (s is NativeMenuItem { CommandParameter: EditorExtension editorExtension } menuItem
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
            .Bind(out ReadOnlyObservableCollection<NativeMenuItem>? list2)
            .Subscribe();

        list2.ForEachItem<NativeMenuItem, ReadOnlyObservableCollection<NativeMenuItem>>(
            editorTabMenu.Items.Insert,
            (i, _) => editorTabMenu.Items.RemoveAt(i),
            editorTabMenu.Items.Clear);

        viewMenuItem.Menu!.Opening += (s, e) =>
        {
            EditorTabItem? selectedTab = EditorService.Current.SelectedTabItem.Value;
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
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        ViewConfig viewConfig = GlobalConfiguration.Instance.ViewConfig;
        viewConfig.WindowSize = ((int)ClientSize.Width, (int)ClientSize.Height);
        viewConfig.WindowPosition = (Position.X, Position.Y);
        viewConfig.IsWindowMaximized = WindowState == WindowState.Maximized;

        if (DataContext is MainViewModel viewModel)
        {
            viewModel.Dispose();
        }
    }
}
