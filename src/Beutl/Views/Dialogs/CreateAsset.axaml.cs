using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

using Beutl.ViewModels.Dialogs;

using FluentAvalonia.UI.Controls;

namespace Beutl.Views.Dialogs;

public partial class CreateAsset : ContentDialog
{
    private bool _flag;

    public CreateAsset()
    {
        InitializeComponent();
        MethodsList.AddHandler(PointerPressedEvent, MethodsList_PointerPressed, RoutingStrategies.Tunnel);
        MethodsList.AddHandler(PointerReleasedEvent, MethodsList_PointerReleased, RoutingStrategies.Tunnel);
    }

    protected override Type StyleKeyOverride => typeof(ContentDialog);

    protected override void OnPrimaryButtonClick(ContentDialogButtonClickEventArgs args)
    {
        base.OnPrimaryButtonClick(args);
        if (DataContext is CreateAssetViewModel viewModel)
        {
            args.Cancel = true;
            Next(viewModel);
        }
    }

    protected override void OnClosing(ContentDialogClosingEventArgs args)
    {
        base.OnClosing(args);
        if (DataContext is CreateAssetViewModel viewModel)
        {
            viewModel.Cancel();
        }
    }

    private async void OpenFile(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CreateAssetViewModel viewModel && VisualRoot is Window parent)
        {
            var options = new FilePickerOpenOptions
            {
                FileTypeFilter = new FilePickerFileType[]
                {
                    viewModel.RequestContentType ?? FilePickerFileTypes.All
                }
            };
            IReadOnlyList<IStorageFile> result = await parent.StorageProvider.OpenFilePickerAsync(options);

            if (result.Count > 0
                && result[0].TryGetLocalPath() is string localPath)
            {
                viewModel.File.Value = localPath;
            }
        }
    }

    private void MethodsList_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_flag)
        {
            if (DataContext is CreateAssetViewModel viewModel && viewModel.IsPrimaryButtonEnabled.Value)
            {
                viewModel.PageIndex.Value = 1;
            }

            _flag = false;
        }
    }

    private void MethodsList_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            _flag = true;
        }
    }

    private static async void Next(CreateAssetViewModel viewModel)
    {
        if (viewModel.PageIndex.Value == 1
            && viewModel.SelectedMethod.Value == 0)
        {
            // 進捗UI
            viewModel.PageIndex.Value = 3;
        }
        else
        {
            viewModel.PageIndex.Value++;
        }

        if (viewModel.PageIndex.Value == 3)
        {
            await viewModel.SubmitAsync();
        }
    }
}
