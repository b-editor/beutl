using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;

using Beutl.Api.Objects;

using Beutl.ViewModels;
using Beutl.ViewModels.ExtensionsPages.DiscoverPages;

using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Navigation;

namespace Beutl.Pages.ExtensionsPages.DiscoverPages;

public partial class PublicPackageDetailsPage : UserControl
{
    public PublicPackageDetailsPage()
    {
        InitializeComponent();
        AddHandler(Frame.NavigatedFromEvent, OnNavigatedFrom, RoutingStrategies.Direct);
        AddHandler(Frame.NavigatedToEvent, OnNavigatedTo, RoutingStrategies.Direct);
    }

    private void OnNavigatedTo(object? sender, NavigationEventArgs e)
    {
        if (e.Parameter is Package package)
        {
            DestoryDataContext();
            DataContextFactory factory = GetDataContextFactory();
            DataContext = factory.PublicPackageDetailPage(package);
        }
    }

    private void OnNavigatedFrom(object? sender, NavigationEventArgs e)
    {
        DestoryDataContext();
    }

    private void DestoryDataContext()
    {
        if (DataContext is PublicPackageDetailsPageViewModel disposable)
        {
            disposable.Dispose();
        }

        DataContext = null;
    }

    private DataContextFactory GetDataContextFactory()
    {
        return ((ExtensionsDialogViewModel)this.FindLogicalAncestorOfType<ExtensionsDialog>()!.DataContext!).Discover.DataContextFactory;
    }

    private async void OpenWebSite_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is PublicPackageDetailsPageViewModel viewModel
            && viewModel.Package.WebSite.Value is string url)
        {
            var dialog = new ContentDialog()
            {
                Title = Language.ExtensionsPage.OpenUrl_Title,
                Content = new SelectableTextBlock()
                {
                    Text = string.Format(Language.ExtensionsPage.OpenUrl_Content, url)
                },
                PrimaryButtonText = Strings.Open,
                CloseButtonText = Strings.Cancel
            };

            if (await dialog.ShowAsync() is ContentDialogResult.Primary)
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true, Verb = "open" });
            }
        }
    }

    private void OpenPublisherPage_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is PublicPackageDetailsPageViewModel viewModel
            && this.FindLogicalAncestorOfType<Frame>() is { } frame)
        {
            frame.Navigate(typeof(UserProfilePage), viewModel.Package.Owner);
        }
    }
}
