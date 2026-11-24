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
using Beutl.Api.Clients;
using Beutl.Configuration;
using Beutl.Controls;
using Beutl.Editor.Components.FileBrowserTab.Services;
using Beutl.Editor.Components.Helpers;
using Beutl.Logging;
using Beutl.Services;
using Beutl.ViewModels;
using Beutl.Views.Tutorial;
using FluentAvalonia.Styling;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Beutl.Views;

public partial class EditorHostFallback : UserControl
{
    private INotificationService Notifications => (DataContext as MainViewModel)?.AppServices.GetService<INotificationService>()
        ?? Beutl.Controls.ControlNotifications.GetOrDefault(this);

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
    private ViewConfig? _configuration;
    private bool _bindingConfiguration;
    private bool _projectsAttached;

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

        layoutTabs.GetObservable(SelectingItemsControl.SelectedIndexProperty).Subscribe(OnLayoutChanged);
        BindConfiguration();

        OnActualThemeVariantChanged(null, EventArgs.Empty);
        ActualThemeVariantChanged += OnActualThemeVariantChanged;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _projectsAttached = true;
        BindConfiguration();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _projectsAttached = false;
        base.OnDetachedFromVisualTree(e);
        if (_configuration != null)
            _configuration.RecentProjects.CollectionChanged -= OnRecentProjectsChanged;
        BindHostedProjects();
        SyncRecentItems();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        BindConfiguration();
    }

    private void BindConfiguration()
    {
        if (layoutTabs == null) return;

        if (_configuration != null)
            _configuration.RecentProjects.CollectionChanged -= OnRecentProjectsChanged;
        _configuration = (DataContext as MainViewModel)?.Configuration.ViewConfig
            ?? ControlConfiguration.GetConfiguration(this)?.ViewConfig;
        _bindingConfiguration = true;
        try
        {
            layoutTabs.SelectedIndex = _configuration?.RecentProjectsLayout.Value == RecentProjectsLayout.List ? 1 : 0;
        }
        finally
        {
            _bindingConfiguration = false;
        }
        if (_configuration != null && _projectsAttached)
            _configuration.RecentProjects.CollectionChanged += OnRecentProjectsChanged;
        BindHostedProjects();
        SyncRecentItems();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ControlConfiguration.ConfigurationProperty)
        {
            BindConfiguration();
        }
        else if (change.Property == IsVisibleProperty)
        {
            BindHostedProjects();
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
        bool wasGrid = recentList.Classes.Contains("grid");
        bool isGrid = layout == RecentProjectsLayout.Grid;
        recentList.Classes.Set("grid", isGrid);
        if (_configuration != null)
            _configuration.RecentProjectsLayout.Value = layout;
        if (!_bindingConfiguration && wasGrid != isGrid && this.IsAttachedToVisualTree() && recentList.ItemCount > 0)
            UiMotion.Reveal(recentList, UiMotion.Normal);
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

    private async void CloneRepository_Click(object? sender, RoutedEventArgs e)
    {
        await ExecuteMainViewModelCommandAsync(vm => vm.MenuBar.CloneRepository.ExecuteAsync(null!));
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
                await ex.Handle(Notifications);
            }
        }
    }

    private void DeleteRecentItem_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is FAMenuFlyoutItem { DataContext: RecentProjectItem { IsRemoteOnly: false } item })
        {
            if (_configuration is not { } viewConfig) return;

            viewConfig.RecentFiles.Remove(item.FullName);
            viewConfig.RecentProjects.Remove(item.FullName);
        }
    }

    private async void DeleteRecentProjectFromDisk_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is FAMenuFlyoutItem { DataContext: RecentProjectItem { IsRemoteOnly: false } item })
        {
            await ExecuteMainViewModelCommandAsync(vm => vm.ProjectDiskDeletion.DeleteAsync(item.FullName));
        }
    }

    private async void OpenRecentItem_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is FAMenuFlyoutItem { DataContext: RecentProjectItem item })
        {
            await OpenRecentProjectAsync(item);
        }
    }

    // Selects the project file in the platform's file manager, or opens its folder where it cannot select a file.
    private async void ShowRecentItemInFileManager_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not FAMenuFlyoutItem { DataContext: RecentProjectItem { IsRemoteOnly: false } item })
            return;

        // A project on a disconnected drive can take a while to answer.
        if (!await Task.Run(() => File.Exists(item.FullName)))
        {
            Notifications.ShowError(FileManagerMenuHeader, MessageStrings.FileDoesNotExist);
            return;
        }

        await FileManagerLauncher.RevealAsync(Notifications, item.FullName, isDirectory: false, LaunchFileManagerAsync, s_logger);
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
            await OpenRecentProjectAsync(pressed);
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
            await OpenRecentProjectAsync(item);
        }
    }

    private async Task OpenRecentProjectAsync(RecentProjectItem item)
    {
        if (OpeningFile != null)
        {
            return;
        }

        await ExecuteMainViewModelCommandAsync(async viewModel =>
        {
            using Activity? activity = Telemetry.StartActivity("EditPageFallback.OpenRecentFile");
            OpeningFile = item.OpeningKey;
            try
            {
                // Let the row render its progress ring before project deserialization starts.
                await Dispatcher.Yield(DispatcherPriority.Background);
                if (item.UnclonedRepositoryId is { } repositoryId)
                {
                    if (TopLevel.GetTopLevel(this) is Window owner)
                        await viewModel.RepositoryClone.OpenAsync(owner, new RepositoryOpenRequest(repositoryId, null));
                }
                else
                {
                    await viewModel.MenuBar.OpenRecentProject.ExecuteAsync(item.FullName);
                }
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
        if (DataContext is not MainViewModel viewModel) return;
        var dialog = new TutorialListDialog(viewModel.AppServices.GetRequiredService<Beutl.Services.Tutorials.ITutorialService>());
        await dialog.ShowAsync();
    }

    private void OnRecentProjectsChanged(object? sender, EventArgs e)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            SyncAfterRecentProjectsChanged();
        }
        else
        {
            Dispatcher.UIThread.Post(SyncAfterRecentProjectsChanged);
        }
    }

    private void SyncAfterRecentProjectsChanged()
    {
        SyncRecentItems();
        RefreshHostedProjects();
    }

    // Holds what the recent projects list asks for, most recent first, and nothing unless the page is on screen: the
    // pictures would only sit in memory behind the editor, and the next showing reads the ones the close just wrote.
    private void SyncRecentItems()
    {
        string[] allRecentFiles = IsShowingProjects && _configuration != null
            ? [.. _configuration.RecentProjects.Distinct(StringComparer.Ordinal)]
            : [];
        var clonedIds = new HashSet<Guid>();
        var files = new List<string>();
        for (int i = 0; i < allRecentFiles.Length; i++)
        {
            string path = allRecentFiles[i];
            bool firstCopy = _localRepositories.TryGetValue(path, out Guid id) && clonedIds.Add(id);
            if (i < MaxShownProjects || firstCopy)
                files.Add(path);
        }

        var hosted = new Dictionary<Guid, GitRepositorySummary>();
        foreach (GitRepositorySummary repository in _hostedRepositories.OrderByDescending(repo => repo.UpdatedAt))
        {
            if (Guid.TryParseExact(repository.Id, "D", out Guid id) && id != Guid.Empty && !clonedIds.Contains(id))
                hosted.TryAdd(id, repository);
        }

        var localPaths = files.ToHashSet(StringComparer.Ordinal);

        for (int i = _recentItems.Count - 1; i >= 0; i--)
        {
            RecentProjectItem item = _recentItems[i];
            bool keep = item.UnclonedRepositoryId is { } id
                ? hosted.TryGetValue(id, out GitRepositorySummary? repo) && item.Name == repo.Name
                : localPaths.Contains(item.FullName);
            if (!keep)
            {
                _recentItems.RemoveAt(i);
                item.Dispose();
            }
        }

        for (int i = 0; i < files.Count; i++)
        {
            string path = files[i];
            int current = IndexOfRecentItem(item => item.FullName == path && !item.IsRemoteOnly);
            if (current < 0)
            {
                var item = new RecentProjectItem(path);
                _recentItems.Insert(i, item);
                _ = item.LoadAsync();
            }
            else if (current != i)
            {
                _recentItems.Move(current, i);
            }
            _recentItems[i].RepositoryLabel.Value = _localRepositories.ContainsKey(path) ? Strings.HostedGit_ClonedProject : null;
        }

        int position = files.Count;
        foreach ((Guid id, GitRepositorySummary repository) in hosted)
        {
            int current = IndexOfRecentItem(item => item.UnclonedRepositoryId == id);
            if (current < 0)
                _recentItems.Insert(position, new RecentProjectItem(id, repository.Name, repository.UpdatedAt));
            else if (current != position)
                _recentItems.Move(current, position);
            position++;
        }
    }

    private int IndexOfRecentItem(Func<RecentProjectItem, bool> match)
    {
        for (int i = 0; i < _recentItems.Count; i++)
        {
            if (match(_recentItems[i]))
            {
                return i;
            }
        }

        return -1;
    }
}
