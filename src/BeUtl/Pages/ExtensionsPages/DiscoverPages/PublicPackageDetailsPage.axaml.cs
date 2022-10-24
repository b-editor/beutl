using AsyncImageLoader;

using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;

using Beutl.Api.Objects;

using BeUtl.ViewModels.ExtensionsPages.DiscoverPages;

using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Navigation;

namespace BeUtl.Pages.ExtensionsPages.DiscoverPages;
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
            DataContext = new PublicPackageDetailsPageViewModel(package);
        }
    }

    private void OnNavigatedFrom(object? sender, NavigationEventArgs e)
    {
        DestoryDataContext();
    }

    private void DestoryDataContext()
    {
        if (DataContext is IDisposable disposable)
        {
            disposable.Dispose();
        }

        DataContext = null;
    }

    private async void OpenWebSite_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is PublicPackageDetailsPageViewModel viewModel)
        {
            string url = viewModel.Package.WebSite.Value;
            var dialog = new ContentDialog()
            {
                Title = "URLを開きますか？",
                Content = new RichTextBlock()
                {
                    IsTextSelectionEnabled = true,
                    Text = $"'{url}'を開こうとしています。\n不審なURLの場合、開かないことをおすすめします。"
                },
                PrimaryButtonText = "開く",
                CloseButtonText = "キャンセル"
            };

            if (await dialog.ShowAsync() is ContentDialogResult.Primary)
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true, Verb = "open" });
            }
        }
    }
}
