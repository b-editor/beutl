using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

using Beutl.ViewModels;

namespace Beutl.Views;

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
}
