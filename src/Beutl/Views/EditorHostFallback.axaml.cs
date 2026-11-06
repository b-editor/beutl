using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Beutl.Configuration;
using Beutl.Editor.Components.FileBrowserTab.Services;
using Beutl.Editor.Components.Helpers;
using Beutl.Logging;
using Beutl.Services;
using Beutl.ViewModels;
using Beutl.Views.Tutorial;
using FluentAvalonia.Styling;

using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.Logging;

namespace Beutl.Views;

public partial class EditorHostFallback : UserControl
{
    public static readonly StyledProperty<string?> OpeningFileProperty =
        AvaloniaProperty.Register<EditorHostFallback, string?>(nameof(OpeningFile));

    // Every picture stays decoded while the page shows (about 0.5 MiB each), and the recent list has no end, so the
    // page shows the most recent ones only. The File menu still lists them all.
    internal const int MaxShownProjects = 100;

    private const double MinGridCellWidth = 232;
    // The margin of the grid's panel, either side.
    private const double GridSideMargin = 16;

    // Fits as many cells of at least MinGridCellWidth as the list has room for and shares what is left between them.
    public static readonly IValueConverter ToGridCellWidth = new FuncValueConverter<double, double>(listWidth =>
    {
        double available = listWidth - (GridSideMargin * 2);
        return available > 0
            ? Math.Floor(available / Math.Max(1, Math.Floor(available / MinGridCellWidth)))
            : double.NaN;
    });

    private static readonly ILogger s_logger = Log.CreateLogger<EditorHostFallback>();

    private readonly ObservableCollection<RecentProjectItem> _recentItems = [];
    private RecentProjectItem? _pressedItem;

    // "Show in Finder", "Show in Explorer" or "Open in File Manager", as the platform calls it.
    public static string FileManagerMenuHeader => FileManagerLauncher.MenuHeader;

    // Tests stand in for the file manager here.
    internal Func<ProcessStartInfo, Task<bool>> LaunchFileManagerAsync { get; set; } = FileManagerLauncher.LaunchAsync;

    public string? OpeningFile
    {
        get => GetValue(OpeningFileProperty);
        private set => SetValue(OpeningFileProperty, value);
    }

    public EditorHostFallback()
    {
        InitializeComponent();
        recentList.ItemsSource = _recentItems;
        recentList.AddHandler(PointerPressedEvent, OnRecentListPointerPressed, RoutingStrategies.Tunnel);
        recentList.AddHandler(PointerReleasedEvent, OnRecentListPointerReleased, RoutingStrategies.Tunnel);
        recentList.AddHandler(KeyDownEvent, OnRecentListKeyDown, RoutingStrategies.Tunnel);

        layoutTabs.SelectedIndex = GlobalConfiguration.Instance.ViewConfig.RecentProjectsLayout.Value == RecentProjectsLayout.List ? 1 : 0;
        layoutTabs.GetObservable(SelectingItemsControl.SelectedIndexProperty).Subscribe(OnLayoutChanged);

        OnActualThemeVariantChanged(null, EventArgs.Empty);
        ActualThemeVariantChanged += OnActualThemeVariantChanged;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        GlobalConfiguration.Instance.ViewConfig.RecentProjects.CollectionChanged += OnRecentProjectsChanged;
        SyncRecentItems();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        GlobalConfiguration.Instance.ViewConfig.RecentProjects.CollectionChanged -= OnRecentProjectsChanged;
        SyncRecentItems();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsVisibleProperty)
        {
            SyncRecentItems();
        }
    }

    private void OnActualThemeVariantChanged(object? sender, EventArgs e)
    {
        ThemeVariant theme = ActualThemeVariant;

        void SetImage(string darkTheme, string lightTheme, Image image)
        {
            using var stream = AssetLoader.Open(theme == ThemeVariant.Light || theme == FluentAvaloniaTheme.HighContrastTheme
                ? new Uri(lightTheme)
                : new Uri(darkTheme));
            image.Source = new Bitmap(stream);
        }

        SetImage(
            darkTheme: "avares://Beutl.Controls/Assets/social/GitHub-Mark-Light-120px-plus.png",
            lightTheme: "avares://Beutl.Controls/Assets/social/GitHub-Mark-120px-plus.png",
            image: githubLogo);

        SetImage(
            darkTheme: "avares://Beutl.Controls/Assets/social/x-logo-white.png",
            lightTheme: "avares://Beutl.Controls/Assets/social/x-logo-black.png",
            image: xLogo);
    }

    private void OnLayoutChanged(int index)
    {
        RecentProjectsLayout layout = index == 1 ? RecentProjectsLayout.List : RecentProjectsLayout.Grid;
        recentList.Classes.Set("grid", layout == RecentProjectsLayout.Grid);
        GlobalConfiguration.Instance.ViewConfig.RecentProjectsLayout.Value = layout;
    }

    private async void CreateNewProject_Click(object? sender, RoutedEventArgs e)
    {
        await ExecuteMainViewModelCommandAsync(
            vm => vm.MenuBar.CreateNewProject.ExecuteAsync(null!));
    }

    private async void OpenProject_Click(object? sender, RoutedEventArgs e)
    {
        await ExecuteMainViewModelCommandAsync(vm => vm.MenuBar.OpenProject.ExecuteAsync(null!));
    }

    private async Task ExecuteMainViewModelCommandAsync(Func<MainViewModel, Task> action)
    {
        if (this.FindAncestorOfType<MainView>() is { DataContext: MainViewModel viewModel })
        {
            try
            {
                await action(viewModel);
            }
            catch (Exception ex)
            {
                await ex.Handle();
            }
        }
    }

    private void DeleteRecentItem_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is FAMenuFlyoutItem { DataContext: RecentProjectItem item })
        {
            ViewConfig viewConfig = GlobalConfiguration.Instance.ViewConfig;

            viewConfig.RecentFiles.Remove(item.FullName);
            viewConfig.RecentProjects.Remove(item.FullName);
        }
    }

    private async void DeleteRecentProjectFromDisk_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is FAMenuFlyoutItem { DataContext: RecentProjectItem item })
        {
            await ExecuteMainViewModelCommandAsync(vm => vm.ProjectDiskDeletion.DeleteAsync(item.FullName));
        }
    }

    private async void OpenRecentItem_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is FAMenuFlyoutItem { DataContext: RecentProjectItem item })
        {
            await OpenRecentProjectAsync(item.FullName);
        }
    }

    // Selects the project file in the platform's file manager, or opens its folder where it cannot select a file.
    private async void ShowRecentItemInFileManager_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not FAMenuFlyoutItem { DataContext: RecentProjectItem item })
            return;

        // A project on a disconnected drive can take a while to answer.
        if (!await Task.Run(() => File.Exists(item.FullName)))
        {
            NotificationService.ShowError(FileManagerMenuHeader, MessageStrings.FileDoesNotExist);
            return;
        }

        try
        {
            if (!await LaunchFileManagerAsync(FileManagerLauncher.CreateStartInfo(item.FullName, isDirectory: false)))
            {
                s_logger.LogWarning("File manager exited unsuccessfully for {Path}", item.FullName);
                NotificationService.ShowError(FileManagerMenuHeader, MessageStrings.OperationFailed);
            }
        }
        catch (Exception ex)
        {
            s_logger.LogError(ex, "Failed to open {Path} in file manager", item.FullName);
            NotificationService.ShowError(FileManagerMenuHeader, MessageStrings.OperationFailed);
        }
    }

    // One click opens a project. The press and the release must land on the same entry, so moving off it before
    // letting go cancels the open.
    private void OnRecentListPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _pressedItem = e.GetCurrentPoint(this).Properties.IsLeftButtonPressed ? FindRecentItem(e.Source) : null;
    }

    private async void OnRecentListPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        RecentProjectItem? pressed = _pressedItem;
        _pressedItem = null;
        if (pressed is not null
            && e.InitialPressMouseButton == MouseButton.Left
            && ReferenceEquals(FindRecentItem(recentList.InputHitTest(e.GetPosition(recentList))), pressed))
        {
            await OpenRecentProjectAsync(pressed.FullName);
        }
    }

    private static RecentProjectItem? FindRecentItem(object? source)
    {
        return (source as Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true)?.DataContext as RecentProjectItem;
    }

    private async void OnRecentListKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && recentList.SelectedItem is RecentProjectItem item)
        {
            e.Handled = true;
            await OpenRecentProjectAsync(item.FullName);
        }
    }

    private async Task OpenRecentProjectAsync(string fileName)
    {
        if (OpeningFile != null)
        {
            return;
        }

        await ExecuteMainViewModelCommandAsync(async viewModel =>
        {
            using Activity? activity = Telemetry.StartActivity("EditPageFallback.OpenRecentFile");
            OpeningFile = fileName;
            try
            {
                // Let the row render its progress ring before project deserialization starts.
                await Dispatcher.Yield(DispatcherPriority.Background);
                await viewModel.MenuBar.OpenRecentProject.ExecuteAsync(fileName);
            }
            finally
            {
                OpeningFile = null;
            }
        });
    }

    private void SocialClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string url })
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true, Verb = "open" });
        }
    }

    private async void StartTutorial_Click(object? sender, RoutedEventArgs e)
    {
        var dialog = new TutorialListDialog();
        await dialog.ShowAsync();
    }

    private void OnRecentProjectsChanged(object? sender, EventArgs e)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            SyncRecentItems();
        }
        else
        {
            Dispatcher.UIThread.Post(SyncRecentItems);
        }
    }

    // Holds what the recent projects list asks for, most recent first, and nothing unless the page is on screen: the
    // pictures would only sit in memory behind the editor, and the next showing reads the ones the close just wrote.
    private void SyncRecentItems()
    {
        string[] files = this.IsAttachedToVisualTree() && IsVisible
            ? [.. GlobalConfiguration.Instance.ViewConfig.RecentProjects.Distinct().Take(MaxShownProjects)]
            : [];

        for (int i = _recentItems.Count - 1; i >= 0; i--)
        {
            RecentProjectItem item = _recentItems[i];
            if (!files.Contains(item.FullName))
            {
                _recentItems.RemoveAt(i);
                item.Dispose();
            }
        }

        for (int i = 0; i < files.Length; i++)
        {
            if (i < _recentItems.Count && _recentItems[i].FullName == files[i])
            {
                continue;
            }

            int current = IndexOfRecentItem(files[i]);
            if (current >= 0)
            {
                _recentItems.Move(current, i);
            }
            else
            {
                var item = new RecentProjectItem(files[i]);
                _recentItems.Insert(i, item);
                _ = item.LoadAsync();
            }
        }
    }

    private int IndexOfRecentItem(string file)
    {
        for (int i = 0; i < _recentItems.Count; i++)
        {
            if (_recentItems[i].FullName == file)
            {
                return i;
            }
        }

        return -1;
    }
}
