using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;

using Beutl.ViewModels.SettingsPages;

using FluentAvalonia.UI.Controls;

namespace Beutl.Pages.SettingsPages;

public sealed partial class EditorExtensionPriorityPage : UserControl
{
    public EditorExtensionPriorityPage()
    {
        InitializeComponent();
    }

    private async void Add_FileExtension(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EditorExtensionPriorityPageViewModel viewModel)
        {
            var dialog = new ContentDialog
            {
                DataContext = viewModel,
                Title = Language.SettingsPage.Add_file_extension,
                PrimaryButtonText = Strings.Add,
                [!ContentDialog.IsPrimaryButtonEnabledProperty] = new Binding("CanAddFileExtension.Value"),
                PrimaryButtonCommand = viewModel.AddFileExtension,
                CloseButtonText = Strings.Cancel,
                Content = new TextBox
                {
                    [!TextBox.TextProperty] = new Binding("FileExtensionInput.Value", BindingMode.TwoWay)
                }
            };
            await dialog.ShowAsync();
        }
    }
}
