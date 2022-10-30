using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;

using BeUtl.Pages.ExtensionsPages.DiscoverPages;
using BeUtl.ViewModels.ExtensionsPages;

using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Navigation;

namespace BeUtl.Pages.ExtensionsPages;

public sealed partial class LibraryPage : UserControl
{
    public LibraryPage()
    {
        InitializeComponent();
        AddHandler(Frame.NavigatedToEvent, OnNavigatedTo, RoutingStrategies.Direct);
    }

    private void OnNavigatedTo(object? sender, NavigationEventArgs e)
    {
        if (e.Parameter is LibraryPageViewModel viewModel)
        {
            DataContext = viewModel;
        }
    }

    private void Package_Click(object? sender, RoutedEventArgs e)
    {
        if (this.FindLogicalAncestorOfType<Frame>() is { } frame
            && DataContext is LibraryPageViewModel viewModel
            && sender is Button button)
        {
            if (button.DataContext is RemoteYourPackageViewModel package)
            {
                frame.Navigate(typeof(PublicPackageDetailsPage), package.Package);
            }
            else if (button.DataContext is LocalYourPackageViewModel localPackage)
            {
                LocalPackage_Click(button, localPackage, viewModel, frame);
            }
            else
            {
                viewModel.More.Execute();
            }
        }
    }

    private static async void LocalPackage_Click(
        Button button,
        LocalYourPackageViewModel localPackage,
        LibraryPageViewModel viewModel,
        Frame frame)
    {
        button.IsEnabled = false;
        var package = await viewModel.TryFindPackage(localPackage.Package);
        button.IsEnabled = true;
        if (package != null)
        {
            frame.Navigate(typeof(PublicPackageDetailsPage), package);
        }
        else
        {
            var dialog = new ContentDialog
            {
                Title = "ローカルパッケージ",
                Content = $"リモートから'{localPackage.Name}'が見つかりませんでした。\n" +
                $"Name: {localPackage.Name}\n" +
                $"DisplayName: {localPackage.DisplayName}\n" +
                $"Publisher: {localPackage.Publisher}\n" +
                $"Description: {localPackage.Package.Description}\n" +
                $"Version: {localPackage.Package.Version}\n" +
                $"WebSite: {localPackage.Package.WebSite}",
                CloseButtonText = "閉じる"
            };
            await dialog.ShowAsync();
        }
    }

    public void ActionButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Command: { } command } button
            && command.CanExecute(button.CommandParameter))
        {
            command.Execute(button.CommandParameter);
            e.Handled = true;
        }
    }

    public void Overflow_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { ContextMenu: { } menu })
        {
            menu.Open();
            e.Handled = true;
        }
    }
}
