using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;

using Beutl.Api.Objects;

using BeUtl.ViewModels.ExtensionsPages.DiscoverPages;

using FluentAvalonia.Styling;
using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Navigation;

namespace BeUtl.Pages.ExtensionsPages.DiscoverPages;

public partial class UserProfilePage : UserControl
{
    private readonly FluentAvaloniaTheme _theme;

    public UserProfilePage()
    {
        InitializeComponent();
        AddHandler(Frame.NavigatedFromEvent, OnNavigatedFrom, RoutingStrategies.Direct);
        AddHandler(Frame.NavigatedToEvent, OnNavigatedTo, RoutingStrategies.Direct);
        _theme = AvaloniaLocator.Current.GetRequiredService<FluentAvaloniaTheme>();
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
