using Avalonia.Controls;
using Avalonia.Interactivity;

using Beutl.ViewModels.SettingsPages;

using FluentAvalonia.UI.Controls;

namespace Beutl.Pages.SettingsPages;

public sealed partial class ViewSettingsPage : UserControl
{
    public ViewSettingsPage()
    {
        InitializeComponent();
    }

    private async void AddPrimaryPropertyClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ViewSettingsPageViewModel viewModel)
        {
            var textBox = new TextBox();
            var dialog = new ContentDialog
            {
                Title = Language.SettingsPage.AddPropertyToHide,
                Content = textBox,
                PrimaryButtonText = Strings.Add,
                CloseButtonText = Strings.Cancel,
                DefaultButton = ContentDialogButton.Primary
            };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary
                && !string.IsNullOrWhiteSpace(textBox.Text))
            {
                viewModel.PrimaryProperties.Add(textBox.Text);
            }
        }
    }
}
