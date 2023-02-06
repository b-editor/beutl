using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Styling;

using Beutl.ViewModels.Dialogs;

using FluentAvalonia.UI.Controls;

namespace Beutl.Pages;

public partial class AddOutputQueueDialog : ContentDialog, IStyleable
{
    private IDisposable? _sBtnBinding;

    public AddOutputQueueDialog()
    {
        InitializeComponent();
    }

    Type IStyleable.StyleKey => typeof(ContentDialog);

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
            _sBtnBinding = this.Bind(IsSecondaryButtonEnabledProperty, vm.CanAdd);
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
                && result[0].Path is { IsFile: true} uri)
            {
                vm.SelectedFile.Value = uri.LocalPath;
            }
        }
    }
}
