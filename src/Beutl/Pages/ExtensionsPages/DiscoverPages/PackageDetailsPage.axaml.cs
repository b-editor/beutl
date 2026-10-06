using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;

using Beutl.Api.Objects;

using Beutl.ViewModels;
using Beutl.ViewModels.ExtensionsPages.DiscoverPages;

using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Navigation;

namespace Beutl.Pages.ExtensionsPages.DiscoverPages;

public partial class PackageDetailsPage : UserControl
{
    public PackageDetailsPage()
    {
        InitializeComponent();
        AddHandler(FAFrame.NavigatedFromEvent, OnNavigatedFrom, RoutingStrategies.Direct);
        AddHandler(FAFrame.NavigatedToEvent, OnNavigatedTo, RoutingStrategies.Direct);
    }

    private void OnNavigatedTo(object? sender, FANavigationEventArgs e)
    {
        if (e.Parameter is Package or PackageDetailsNavigation)
        {
            var navigation = e.Parameter as PackageDetailsNavigation
                ?? new PackageDetailsNavigation((Package)e.Parameter, null);
            DestroyDataContext();
            DataContextFactory factory = GetDataContextFactory();
            DataContext = factory.PackageDetailPage(navigation.Package, navigation.Version);
        }
    }

    private void OnNavigatedFrom(object? sender, FANavigationEventArgs e)
    {
        DestroyDataContext();
    }

    private void DestroyDataContext()
    {
        DiscoverPageHelper.DestroyDataContext<PackageDetailsPageViewModel>(this);
    }

    private DataContextFactory GetDataContextFactory()
    {
        return DiscoverPageHelper.GetDataContextFactory(this);
    }

    private async void OpenWebSite_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is PackageDetailsPageViewModel viewModel
            && viewModel.Package.WebSite.Value is string url)
        {
            var dialog = new FAContentDialog()
            {
                Title = ExtensionsStrings.OpenUrl_Title,
                Content = new SelectableTextBlock()
                {
                    Text = string.Format(ExtensionsStrings.OpenUrl_Content, url)
                },
                PrimaryButtonText = Strings.Open,
                CloseButtonText = Strings.Cancel
            };

            if (await dialog.ShowAsync() is FAContentDialogResult.Primary)
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true, Verb = "open" });
            }
        }
    }

    private void OpenPublisherPage_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is PackageDetailsPageViewModel viewModel
            && this.FindLogicalAncestorOfType<FAFrame>() is { } frame)
        {
            frame.Navigate(typeof(UserProfilePage), viewModel.Package.Owner);
        }
    }
}
