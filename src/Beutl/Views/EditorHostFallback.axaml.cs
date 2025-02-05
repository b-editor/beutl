﻿using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Beutl.Configuration;
using Beutl.Helpers;
using Beutl.Models;
using Beutl.Services;
using Beutl.ViewModels;
using DynamicData;
using DynamicData.Binding;
using FluentAvalonia.Styling;
using FluentAvalonia.UI.Controls;

namespace Beutl.Views;

public partial class EditorHostFallback : UserControl
{
    private bool _flag;

    public EditorHostFallback()
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
                timer?.Dispose();
                closeDialog = ShowWaitDialog(fileName);
                activity?.AddEvent(new("WaitDialogShown"));
            }, null, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(3));

            try
            {
                if (fileName.EndsWith($".{Constants.ProjectFileExtension}", StringComparison.OrdinalIgnoreCase))
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
                case "X":
                    OpenBrowser("https://x.com/yuto_daisensei");
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
                        || (f == 1 && x.EndsWith($".{Constants.ProjectFileExtension}", StringComparison.OrdinalIgnoreCase))
                        || (f == 2 && !x.EndsWith($".{Constants.ProjectFileExtension}", StringComparison.OrdinalIgnoreCase))))
            .AddKey(x => x)
            .Cast(x => new FileInfo(x))
            .SortBy(x => x.LastAccessTimeUtc, sortOrder: SortDirection.Descending)
            .Bind(out ReadOnlyObservableCollection<FileInfo>? list)
            .Subscribe();

        recentList.ItemsSource = list;
    }
}
