using Avalonia.Controls;
using Avalonia.Interactivity;
using Beutl.ViewModels.Dialogs;
using FluentAvalonia.UI.Controls;

namespace Beutl.Views.Dialogs;

internal sealed partial class CloneRepositoryDialog : FAContentDialog
{
    public CloneRepositoryDialog()
    {
        InitializeComponent();
        Closing += (_, args) =>
        {
            if (DataContext is CloneRepositoryDialogViewModel { IsBusy.Value: true } vm)
            {
                args.Cancel = true;
                vm.CancelClone();
            }
        };
    }

    protected override Type StyleKeyOverride => typeof(FAContentDialog);

    protected override async void OnPrimaryButtonClick(FAContentDialogButtonClickEventArgs args)
    {
        base.OnPrimaryButtonClick(args);
        // Keep the dialog's Close button available while the Git process is running.
        args.Cancel = true;
        if (DataContext is CloneRepositoryDialogViewModel vm)
        {
            await Task.Yield();
            if (await vm.CloneAsync())
                Hide(FAContentDialogResult.Primary);
            else if (vm.CancelRequested)
                Hide();
        }
    }

    private async void PickFolder_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CloneRepositoryDialogViewModel vm && await LocationPicker.PickFolderAsync(this) is { } path)
            vm.ParentFolder.Value = path;
    }

    private async void DownloadGit_Click(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is { } owner)
            await owner.Launcher.LaunchUriAsync(new Uri("https://git-scm.com/downloads"));
    }
}
