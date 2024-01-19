using System.Collections.ObjectModel;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;

using Beutl.Configuration;
using Beutl.Helpers;
using Beutl.Models;
using Beutl.Services;
using Beutl.ViewModels;
using Beutl.Views;

using DynamicData;
using DynamicData.Alias;
using DynamicData.Binding;

using FluentAvalonia.Styling;
using FluentAvalonia.UI.Controls;

using Serilog;

namespace Beutl.Pages;

public partial class EditPageFallback : UserControl
{
    private bool _flag;

    public EditPageFallback()
    {
        InitializeComponent();
        recentList.AddHandler(PointerPressedEvent, OnRecentListPointerPressed, RoutingStrategies.Tunnel);
        recentList.AddHandler(PointerReleasedEvent, OnRecentListPointerReleased, RoutingStrategies.Tunnel);

        OnActualThemeVariantChanged(null, EventArgs.Empty);
        ActualThemeVariantChanged += OnActualThemeVariantChanged;

        InitRecentItems();
    }

    private void OnActualThemeVariantChanged(object? sender, EventArgs e)
    {
        ThemeVariant theme = ActualThemeVariant;

        void SetImage(string darkTheme, string lightTheme, BitmapIcon image)
        {
            image.UriSource = theme == ThemeVariant.Light || theme == FluentAvaloniaTheme.HighContrastTheme
                ? new Uri(lightTheme)
                : new Uri(darkTheme);
        }

        SetImage(
            darkTheme: "avares://Beutl/Assets/social/GitHub-Mark-Light-120px-plus.png",
            lightTheme: "avares://Beutl/Assets/social/GitHub-Mark-120px-plus.png",
            image: githubLogo);

        SetImage(
            darkTheme: "avares://Beutl/Assets/social/x-logo-white.png",
            lightTheme: "avares://Beutl/Assets/social/x-logo-black.png",
            image: xLogo);
    }

    private void OpenContext(object? sender, RoutedEventArgs e)
    {
        if (sender is Control control)
        {
            control.ContextMenu?.Open();
        }
    }

    private void CreateNewProject_Click(object? sender, RoutedEventArgs e)
    {
        ExecuteMainViewModelCommand(vm => vm.MenuBar.CreateNewProject.Execute());
    }

    private void CreateNewScene_Click(object? sender, RoutedEventArgs e)
    {
        ExecuteMainViewModelCommand(vm => vm.MenuBar.CreateNew.Execute());
    }

    private void OpenProject_Click(object? sender, RoutedEventArgs e)
    {
        ExecuteMainViewModelCommand(vm => vm.MenuBar.OpenProject.Execute());
    }

    private void OpenFile_Click(object? sender, RoutedEventArgs e)
    {
        ExecuteMainViewModelCommand(vm => vm.MenuBar.OpenFile.Execute());
    }

    private void ExecuteMainViewModelCommand(Action<MainViewModel> action)
    {
        if (this.FindAncestorOfType<MainView>() is { DataContext: MainViewModel viewModel })
        {
            action(viewModel);
        }
    }

    private void DeleteRecentItem_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: FileInfo fi })
        {
            ViewConfig viewConfig = GlobalConfiguration.Instance.ViewConfig;

            viewConfig.RecentFiles.Remove(fi.FullName);
            viewConfig.RecentProjects.Remove(fi.FullName);
        }
    }

    private void OpenRecentItem_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: FileInfo fi })
        {
            OpenRecentFile(fi.FullName);
        }
    }

    private void OnRecentListPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.ClickCount == 2 && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _flag = true;
        }
    }

    private void OnRecentListPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_flag)
        {
            if (recentList.SelectedItem is FileInfo selectedItem)
            {
                OpenRecentFile(selectedItem.FullName);
            }

            _flag = false;
        }
    }

    private static IDisposable ShowWaitDialog(string projectFile)
    {
        return OutProcessDialog.Show(
            title: Message.OpeningProject,
            subtitle: Message.PleaseWaitAMoment,
            content: string.Format(Message.OpeningProjectMessage, Path.GetFileName(projectFile)),
            icon: "Info",
            progress: true);
    }

    private void OpenRecentFile(string fileName)
    {
        ExecuteMainViewModelCommand(viewModel =>
        {
            using Activity? activity = Telemetry.StartActivity("EditPageFallback.OpenRecentFile");

            ITimer? timer = null;
            IDisposable? closeDialog = null;
            timer = TimeProvider.System.CreateTimer(_ =>
            {
                closeDialog = ShowWaitDialog(fileName);
                activity?.AddEvent(new("WaitDialogShown"));
                timer?.Dispose();
            }, null, TimeSpan.Zero, TimeSpan.FromSeconds(3));

            try
            {
                if (fileName.EndsWith($".{Constants.ProjectFileExtension}"))
                {
                    viewModel.MenuBar.OpenRecentProject.Execute(fileName);
                }
                else
                {
                    viewModel.MenuBar.OpenRecentFile.Execute(fileName);
                }
            }
            finally
            {
                Dispatcher.UIThread.Invoke(() =>
                {
                    activity?.AddEvent(new("InputResumed"));
                    timer?.Dispose();
                    closeDialog?.Dispose();
                }, DispatcherPriority.Input);
            }
        });
    }

    private void SocialClick(object? sender, RoutedEventArgs e)
    {
        static void OpenBrowser(string? url)
        {
            if (!string.IsNullOrWhiteSpace(url))
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true, Verb = "open" });
            }
        }

        if (sender is Button { Tag: string tag })
        {
            switch (tag)
            {
                case "GitHub":
                    OpenBrowser("https://github.com/b-editor/beutl");
                    break;
                case "Twitter":
                    OpenBrowser("https://twitter.com/indigo_san_");
                    break;
                case "Url":
                    OpenBrowser("https://github.com/b-editor");
                    break;
            }
        }
    }

    private void InitRecentItems()
    {
        ViewConfig viewConfig = GlobalConfiguration.Instance.ViewConfig;

        IObservable<int> filter = FilterComboBox.GetObservable(SelectingItemsControl.SelectedIndexProperty);

        viewConfig.RecentFiles.ToObservableChangeSet<CoreList<string>, string>()
            .Filter(filter.Select<int, Func<string, bool>>(
                f => (x) => f == 0
                        || (f == 1 && x.EndsWith($".{Constants.ProjectFileExtension}"))
                        || (f == 2 && !x.EndsWith($".{Constants.ProjectFileExtension}"))))
            .AddKey(x => x)
            .Cast(x => new FileInfo(x))
            .SortBy(x => x.LastAccessTimeUtc, sortOrder: SortDirection.Descending)
            .Bind(out ReadOnlyObservableCollection<FileInfo>? list)
            .Subscribe();

        recentList.ItemsSource = list;
    }
}
