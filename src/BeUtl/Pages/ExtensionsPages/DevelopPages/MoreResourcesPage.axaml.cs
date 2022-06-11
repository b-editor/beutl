using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

using BeUtl.Pages.ExtensionsPages.DevelopPages.Dialogs;
using BeUtl.ViewModels.ExtensionsPages.DevelopPages;

using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Media.Animation;

namespace BeUtl.Pages.ExtensionsPages.DevelopPages;

public partial class MoreResourcesPage : UserControl
{
    private MoreResourcesPageViewModel? _viewModel;

    public MoreResourcesPage()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is MoreResourcesPageViewModel viewModel)
        {
            _viewModel?.Dispose();
            viewModel.Initialize();
            _viewModel = viewModel;
        }
    }

    protected override void OnDetachedFromLogicalTree(Avalonia.LogicalTree.LogicalTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromLogicalTree(e);

        _viewModel?.Dispose();
        DataContext = _viewModel = null;
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
