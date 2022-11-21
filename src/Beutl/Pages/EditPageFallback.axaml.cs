using System.Collections.ObjectModel;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

using Beutl.Configuration;
using Beutl.Models;
using Beutl.ViewModels;
using Beutl.Views;

using DynamicData;
using DynamicData.Alias;
using DynamicData.Binding;

using FluentAvalonia.Styling;

namespace Beutl.Pages;

public partial class EditPageFallback : UserControl
{
    private readonly FluentAvaloniaTheme _theme;
    private bool _flag;

    public EditPageFallback()
    {
        InitializeComponent();
        recentList.AddHandler(PointerPressedEvent, OnRecentListPointerPressed, RoutingStrategies.Tunnel);
        recentList.AddHandler(PointerReleasedEvent, OnRecentListPointerReleased, RoutingStrategies.Tunnel);
        _theme = AvaloniaLocator.Current.GetRequiredService<FluentAvaloniaTheme>();

        InitRecentItems();
    }

    protected override void OnAttachedToLogicalTree(Avalonia.LogicalTree.LogicalTreeAttachmentEventArgs e)
    {
        base.OnAttachedToLogicalTree(e);
        _theme.RequestedThemeChanged += Theme_RequestedThemeChanged;
        OnThemeChanged(_theme.RequestedTheme);
    }

    protected override void OnDetachedFromLogicalTree(Avalonia.LogicalTree.LogicalTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromLogicalTree(e);
        _theme.RequestedThemeChanged -= Theme_RequestedThemeChanged;
    }

    private void Theme_RequestedThemeChanged(FluentAvaloniaTheme sender, RequestedThemeChangedEventArgs args)
    {
        OnThemeChanged(args.NewTheme);
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
        ExecuteMainViewModelCommand(vm => vm.CreateNewProject.Execute());
    }

    private void CreateNewScene_Click(object? sender, RoutedEventArgs e)
    {
        ExecuteMainViewModelCommand(vm => vm.CreateNew.Execute());
    }

    private void OpenProject_Click(object? sender, RoutedEventArgs e)
    {
        ExecuteMainViewModelCommand(vm => vm.OpenProject.Execute());
    }

    private void OpenFile_Click(object? sender, RoutedEventArgs e)
    {
        ExecuteMainViewModelCommand(vm => vm.OpenFile.Execute());
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

    private void OnThemeChanged(string theme)
    {
        switch (theme)
        {
            case "Light" or "HightContrast":
                githubLightLogo.IsVisible = true;
                githubDarkLogo.IsVisible = false;
                break;
            case "Dark":
                githubLightLogo.IsVisible = false;
                githubDarkLogo.IsVisible = true;
                break;
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

    private void OpenRecentFile(string fileName)
    {
        ExecuteMainViewModelCommand(viewModel =>
        {
            if (fileName.EndsWith($".{Constants.ProjectFileExtension}"))
            {
                viewModel.OpenRecentProject.Execute(fileName);
            }
            else
            {
                viewModel.OpenRecentFile.Execute(fileName);
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
            .SortBy(x => x.LastAccessTimeUtc)
            .Bind(out ReadOnlyObservableCollection<FileInfo>? list)
            .Subscribe();

        recentList.Items = list;
    }
}
