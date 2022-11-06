using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;

using Beutl.ViewModels.SettingsPages;

using FluentAvalonia.UI.Controls;

namespace Beutl.Pages.SettingsPages;

public sealed partial class ExtensionsSettingsPage : UserControl
{
    public ExtensionsSettingsPage()
    {
        InitializeComponent();
    }

    private async void Add_FileExtension(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ExtensionsSettingsPageViewModel viewModel)
        {
            var dialog = new ContentDialog
            {
                DataContext = viewModel,
                Title = S.ExtensionsSettingsPage.EditorExtensionPriority.Dialog1.Title,
                PrimaryButtonText = S.Common.Add,
                [!ContentDialog.IsPrimaryButtonEnabledProperty] = new Binding("CanAddFileExtension.Value"),
                PrimaryButtonCommand = viewModel.AddFileExtension,
                CloseButtonText = S.Common.Cancel,
                Content = new TextBox
                {
                    [!TextBox.TextProperty] = new Binding("FileExtensionInput.Value", BindingMode.TwoWay)
                }
            };
            await dialog.ShowAsync();
        }
    }
}
