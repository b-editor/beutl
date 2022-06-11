using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

using BeUtl.Pages.ExtensionsPages.DevelopPages.Dialogs;
using BeUtl.ViewModels.ExtensionsPages.DevelopPages;

using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Media.Animation;

namespace BeUtl.Pages.ExtensionsPages.DevelopPages;

public partial class MoreResourcesPage : UserControl
{
    private bool flag;

    public MoreResourcesPage()
    {
        InitializeComponent();
        ResourcesList.AddHandler(PointerPressedEvent, ResourcesList_PointerPressed, RoutingStrategies.Tunnel);
        ResourcesList.AddHandler(PointerReleasedEvent, ResourcesList_PointerReleased, RoutingStrategies.Tunnel);
    }

    private void ResourcesList_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (flag)
        {
            if (ResourcesList.SelectedItem is ResourcePageViewModel item)
            {
                Frame frame = this.FindAncestorOfType<Frame>();
                frame.Navigate(typeof(ResourcePage), item);
            }
            flag = false;
        }
    }

    private void ResourcesList_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            flag = true;
        }
    }

    private async void Add_Click(object? sender, RoutedEventArgs e)
    {
        var dialog = new AddResourceDialog
        {
            DataContext = DataContext
        };
        await dialog.ShowAsync();
    }

    private void Edit_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is StyledElement { DataContext: ResourcePageViewModel item })
        {
            Frame frame = this.FindAncestorOfType<Frame>();
            frame.Navigate(typeof(ResourcePage), item);
        }
    }

    private async void Delete_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is StyledElement { DataContext: ResourcePageViewModel item })
        {
            var dialog = new ContentDialog
            {
                Title = "リソースを削除",
                Content = "リソースを削除してもよろしいですか？",
                PrimaryButtonText = "はい",
                CloseButtonText = "いいえ",
                DefaultButton = ContentDialogButton.Primary
            };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                item.Delete.Execute();
            }
        }
    }

    private void NavigatePackagePage_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MoreResourcesPageViewModel viewModel)
        {
            Frame frame = this.FindAncestorOfType<Frame>();
            var transitionInfo = new EntranceNavigationTransitionInfo
            {
                FromHorizontalOffset = -28,
                FromVerticalOffset = 0
            };

            frame.Navigate(typeof(PackagePage), viewModel.Parent, transitionInfo);
        }
    }
}
