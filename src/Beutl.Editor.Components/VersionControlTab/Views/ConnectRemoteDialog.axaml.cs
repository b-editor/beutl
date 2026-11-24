using Avalonia.Interactivity;
using Beutl.Editor.Components.VersionControlTab.ViewModels;
using FluentAvalonia.UI.Controls;

namespace Beutl.Editor.Components.VersionControlTab.Views;

internal sealed partial class ConnectRemoteDialog : FAContentDialog
{
    public ConnectRemoteDialog()
    {
        InitializeComponent();
        Closing += (_, args) =>
        {
            if (DataContext is ConnectRemoteDialogViewModel { IsBusy.Value: true } vm)
            {
                args.Cancel = true;
                vm.Cancel();
            }
        };
    }

    protected override Type StyleKeyOverride => typeof(FAContentDialog);

    protected override async void OnPrimaryButtonClick(FAContentDialogButtonClickEventArgs args)
    {
        base.OnPrimaryButtonClick(args);
        args.Cancel = true;
        if (DataContext is ConnectRemoteDialogViewModel vm)
        {
            await Task.Yield();
            if (await vm.ConnectAsync())
                Hide(FAContentDialogResult.Primary);
            else if (vm.IsCanceled)
                Hide();
        }
    }

    private async void SignIn_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ConnectRemoteDialogViewModel vm)
            await vm.Picker.SignInAsync();
    }
}
