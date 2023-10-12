using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;

using Beutl.Api.Objects;

using Beutl.Pages.ExtensionsPages.DevelopPages.Dialogs;
using Beutl.ViewModels;
using Beutl.ViewModels.ExtensionsPages.DevelopPages;
using Beutl.ViewModels.ExtensionsPages.DevelopPages.Dialogs;

using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Navigation;

namespace Beutl.Pages.ExtensionsPages.DevelopPages;

public sealed partial class PackageReleasesPage : UserControl
{
    private bool _flag;

    public PackageReleasesPage()
    {
        InitializeComponent();
        ReleasesList.AddHandler(PointerPressedEvent, OnReleasesListPointerPressed, RoutingStrategies.Tunnel);
        ReleasesList.AddHandler(PointerReleasedEvent, OnReleasesListPointerReleased, RoutingStrategies.Tunnel);
        AddHandler(Frame.NavigatedFromEvent, OnNavigatedFrom, RoutingStrategies.Direct);
        AddHandler(Frame.NavigatedToEvent, OnNavigatedTo, RoutingStrategies.Direct);
    }

    private void OnNavigatedTo(object? sender, NavigationEventArgs e)
    {
        if (e.Parameter is Package package)
        {
            DestoryDataContext();
            DataContextFactory factory = GetDataContextFactory();
            DataContext = factory.PackageReleasesPage(package);
        }
    }

    private void OnNavigatedFrom(object? sender, NavigationEventArgs e)
    {
        DestoryDataContext();
    }

    private void DestoryDataContext()
    {
        if (DataContext is PackageReleasesPageViewModel disposable)
        {
            disposable.Dispose();
        }

        DataContext = null;
    }

    private DataContextFactory GetDataContextFactory()
    {
        return ((ExtensionsPageViewModel)this.FindLogicalAncestorOfType<ExtensionsPage>()!.DataContext!).Develop.DataContextFactory;
    }

    private void OnReleasesListPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_flag)
        {
            if (ReleasesList.SelectedItem is Release item)
            {
                NavigateToReleasePage(item);
            }

            _flag = false;
        }
    }

    private void OnReleasesListPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            _flag = true;
        }
    }

    private async void Add_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is PackageReleasesPageViewModel viewModel)
        {
            DataContextFactory factory = GetDataContextFactory();
            AddReleaseDialogViewModel dialogViewModel = factory.AddReleaseDialog(viewModel.Package);
            var dialog = new AddReleaseDialog
            {
                DataContext = dialogViewModel
            };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary
                && dialogViewModel.Result != null)
            {
                viewModel.Items.OrderedAddDescending(dialogViewModel.Result, x => ((Release)x).Version.Value);
            }
        }
    }

    private void Edit_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is StyledElement { DataContext: Release item })
        {
            NavigateToReleasePage(item);
        }
    }

    private void NavigateToReleasePage(Release release)
    {
        if (this.FindAncestorOfType<Frame>() is { } frame)
        {
            frame.Navigate(typeof(ReleasePage), release, SharedNavigationTransitionInfo.Instance);
        }
    }

    private async void Delete_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is PackageReleasesPageViewModel viewModel
            && sender is StyledElement { DataContext: Release release }
            && this.FindAncestorOfType<Frame>() is { } frame)
        {
            var dialog = new ContentDialog
            {
                Title = Language.ExtensionsPage.DeleteRelease_Title,
                Content = Language.ExtensionsPage.DeleteRelease_Content,
                PrimaryButtonText = Strings.Yes,
                CloseButtonText = Strings.No,
                DefaultButton = ContentDialogButton.Primary
            };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                frame.RemoveAllStack(item => item is Release p && p.Id == release.Id);

                await viewModel.DeleteReleaseAsync(release);
                frame.GoBack();
            }
        }
    }

    private void NavigatePackageDetailsPage_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is PackageReleasesPageViewModel viewModel
            && this.FindAncestorOfType<Frame>() is { } frame)
        {
            frame.Navigate(typeof(PackageDetailsPage), viewModel.Package, SharedNavigationTransitionInfo.Instance);
        }
    }
}
