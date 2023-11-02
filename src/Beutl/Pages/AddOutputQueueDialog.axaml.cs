using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

using Beutl.ViewModels.Dialogs;

using FluentAvalonia.UI.Controls;

namespace Beutl.Pages;

public partial class AddOutputQueueDialog : ContentDialog
{
    private IDisposable? _sBtnBinding;

    public AddOutputQueueDialog()
    {
        InitializeComponent();
    }

    protected override Type StyleKeyOverride => typeof(ContentDialog);

    protected override void OnPrimaryButtonClick(ContentDialogButtonClickEventArgs args)
    {
        base.OnPrimaryButtonClick(args);
        if (carousel.SelectedIndex == 1)
        {
            args.Cancel = true;
            IsPrimaryButtonEnabled = false;
            _sBtnBinding?.Dispose();
            SecondaryButtonText = Strings.Next;
            IsSecondaryButtonEnabled = true;
            carousel.Previous();
        }
    }

    protected override void OnSecondaryButtonClick(ContentDialogButtonClickEventArgs args)
    {
        base.OnSecondaryButtonClick(args);
        if (DataContext is not AddOutputQueueViewModel vm) return;

        if (carousel.SelectedIndex == 1)
        {
            vm.Add();
        }
        else
        {
            args.Cancel = true;

            IsPrimaryButtonEnabled = true;
            _sBtnBinding = Bind(IsSecondaryButtonEnabledProperty, vm.CanAdd);
            SecondaryButtonText = Strings.Add;
            carousel.Next();
        }
    }

    // 場所を選択
    private async void OpenFileClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is AddOutputQueueViewModel vm && VisualRoot is TopLevel parent)
        {
            var options = new FilePickerOpenOptions()
            {
                FileTypeFilter = vm.GetFilePickerFileTypes()
            };
            IReadOnlyList<IStorageFile> result = await parent.StorageProvider.OpenFilePickerAsync(options);

            if (result.Count > 0
                && result[0].TryGetLocalPath() is string localPath)
            {
                vm.SelectedFile.Value = localPath;
            }
        }
    }
}
