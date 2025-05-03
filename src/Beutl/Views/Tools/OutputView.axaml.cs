using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Beutl.ViewModels.Tools;
using Beutl.Views.Dialogs;

namespace Beutl.Views.Tools;

public partial class OutputView : UserControl
{
    public OutputView()
    {
        InitializeComponent();
    }

    private async void SelectDestinationFileClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is OutputViewModel viewModel
            && VisualRoot is TopLevel topLevel)
        {
            var options = new FilePickerSaveOptions()
            {
                FileTypeChoices = OutputViewModel.GetFilePickerFileTypes()
            };
            IStorageFile? file = await topLevel.StorageProvider.SaveFilePickerAsync(options);

            if (file != null && file.TryGetLocalPath() is string localPath)
            {
                viewModel.DestinationFile.Value = localPath;
                file.Dispose();
            }
        }
    }

    private async void StartEncodeClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not OutputViewModel viewModel) return;

        var dialog = new OutputProgressDialog { DataContext = viewModel };
        _ = dialog.ShowAsync();
        await viewModel.StartEncode();
        dialog.Hide();
    }
}
