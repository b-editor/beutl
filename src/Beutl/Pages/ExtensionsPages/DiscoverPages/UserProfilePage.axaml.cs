using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Styling;

using Beutl.Api.Objects;

using Beutl.ViewModels.ExtensionsPages.DiscoverPages;

using FluentAvalonia.Styling;
using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Navigation;

namespace Beutl.Pages.ExtensionsPages.DiscoverPages;

public partial class UserProfilePage : UserControl
{
    public UserProfilePage()
    {
        InitializeComponent();
        AddHandler(Frame.NavigatedFromEvent, OnNavigatedFrom, RoutingStrategies.Direct);
        AddHandler(Frame.NavigatedToEvent, OnNavigatedTo, RoutingStrategies.Direct);

        OnActualThemeVariantChanged(null, EventArgs.Empty);
        ActualThemeVariantChanged += OnActualThemeVariantChanged;
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
            darkTheme: "avares://Beutl.Controls/Assets/social/GitHub-Mark-Light-120px-plus.png",
            lightTheme: "avares://Beutl.Controls/Assets/social/GitHub-Mark-120px-plus.png",
            image: githubLogo);

        SetImage(
            darkTheme: "avares://Beutl.Controls/Assets/social/x-logo-white.png",
            lightTheme: "avares://Beutl.Controls/Assets/social/x-logo-black.png",
            image: xLogo);
    }

    private void OnNavigatedTo(object? sender, NavigationEventArgs e)
    {
        if (e.Parameter is Profile user)
        {
            DestoryDataContext();
            DataContext = new UserProfilePageViewModel(user);
        }
    }

    private void OnNavigatedFrom(object? sender, NavigationEventArgs e)
    {
        DestoryDataContext();
    }

    private void DestoryDataContext()
    {
        if (DataContext is UserProfilePageViewModel disposable)
        {
            disposable.Dispose();
        }

        DataContext = null;
    }

    private void Package_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: Package package }
            && this.FindLogicalAncestorOfType<Frame>() is { } frame)
        {
            frame.Navigate(typeof(PublicPackageDetailsPage), package);
        }
        else if (DataContext is UserProfilePageViewModel viewModel)
        {
            viewModel.More.Execute();
        }
    }

    private void OpenSocial_Click(object? sender, RoutedEventArgs e)
    {
        static async void OpenBrowser(string? url, bool showDialog = false)
        {
            if (!string.IsNullOrWhiteSpace(url))
            {
                if (showDialog)
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
                else
                {
                    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true, Verb = "open" });
                }
            }
        }

        static void OpenMailClient(string? address)
        {
            if (address != null)
            {
                Process.Start(new ProcessStartInfo($"mailto:{address}") { UseShellExecute = true, Verb = "open" });
            }
        }

        if (DataContext is UserProfilePageViewModel viewModel
            && sender is Button { Tag: string tag })
        {
            switch (tag)
            {
                case "GitHub":
                    OpenBrowser(viewModel.GitHubUrl.Value);
                    break;
                case "Twitter":
                    OpenBrowser(viewModel.TwitterUrl.Value);
                    break;
                case "YouTube":
                    OpenBrowser(viewModel.YouTubeUrl.Value);
                    break;
                case "Blog":
                    OpenBrowser(viewModel.BlogUrl.Value, true);
                    break;
                case "Email":
                    OpenMailClient(viewModel.Email.Value);
                    break;
            }
        }
    }
}
