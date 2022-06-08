using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;

using BeUtl.ViewModels.SettingsPages;

using Firebase.Auth;
using Firebase.Auth.UI;

using FluentAvalonia.UI.Controls;

using S = BeUtl.Language.StringResources;

namespace BeUtl.Pages.SettingsPages;

public sealed partial class AccountSettingsPage : UserControl
{
    private TaskCompletionSource<UserCredential?>? _userCredential;

    public AccountSettingsPage()
    {
        InitializeComponent();
    }

    private async void UploadProfileImage_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is AccountSettingsPageViewModel viewModel
            && viewModel.User.Value is not null)
        {
            Window? window = this.FindLogicalAncestorOfType<Window>();
            var dialog = new OpenFileDialog
            {
                AllowMultiple = false,
                Filters =
                {
                    new FileDialogFilter()
                    {
                        Extensions = { "jpg", "jpeg", "png" }
                    }
                }
            };
            if ((await dialog.ShowAsync(window)) is string[] items && items.Length > 0)
            {
                viewModel.UploadPhotoImage.Execute(items[0]);
            }
        }
    }

    private void DeleteAccount_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is AccountSettingsPageViewModel viewModel
            && viewModel.User.Value is User user)
        {
            switch ((viewModel.SignInWithGoogle.Value, viewModel.SignInWithEmail.Value))
            {
                case (true, _):
                    DeleteAccountGoogle(viewModel, user);
                    break;
                case (_, true):
                    DeleteAccountEmail(viewModel, user);
                    break;
            }
        }
    }

    private async void SignInWithGoogleClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            UserCredential userCredential = await FirebaseUI.Instance.SignInAsync(firebaseControl, FirebaseProviderType.Google);
            _userCredential!.SetResult(userCredential);
            dialog1Google.IsPrimaryButtonEnabled = userCredential != null;
            dialog1GoogleSignIn.IsEnabled = userCredential == null;
        }
        catch
        {
            _userCredential!.SetResult(null);
            dialog1Google.IsPrimaryButtonEnabled = false;
            dialog1GoogleSignIn.IsEnabled = true;
        }
    }

    private async void DeleteAccountGoogle(AccountSettingsPageViewModel viewModel, User user)
    {
        dialog1Google.IsPrimaryButtonEnabled = false;
        dialog1GoogleSignIn.IsEnabled = true;
        dialog1GoogleText.Text = string.Format(S.AccountSettingsPage.Dialog1.Content, user.Info.Email);

    Label1:
        _userCredential = new TaskCompletionSource<UserCredential?>();
        Task<ContentDialogResult> showDialogTask = dialog1Google.ShowAsync();

        switch (await showDialogTask)
        {
            case ContentDialogResult.None:
                break;
            case ContentDialogResult.Primary:
                try
                {
                    UserCredential credential = _userCredential.Task.Result!;
                    viewModel.DeleteAccount.Execute(credential.User);
                }
                catch (FirebaseAuthException ex)
                {
                    dialog1Error.Text = FirebaseErrorLookup.LookupError(ex);
                    dialog1Error.Opacity = 1;
                    goto Label1;
                }
                break;
            case ContentDialogResult.Secondary:
                DeleteAccountEmail(viewModel, user);
                break;
        }
    }

    private async void DeleteAccountEmail(AccountSettingsPageViewModel viewModel, User user)
    {
        dialog1Text.Text = string.Format(S.AccountSettingsPage.Dialog1.Content, user.Info.Email);

    Label1:
        switch (await dialog1.ShowAsync())
        {
            case ContentDialogResult.None:
                break;
            case ContentDialogResult.Primary:
                try
                {
                    UserCredential credential = await FirebaseUI.Instance.Client.SignInWithEmailAndPasswordAsync(user.Info.Email, dialog1Password.Text);
                    viewModel.DeleteAccount.Execute(credential.User);
                }
                catch (FirebaseAuthException ex)
                {
                    dialog1Error.Text = FirebaseErrorLookup.LookupError(ex);
                    dialog1Error.Opacity = 1;
                    goto Label1;
                }
                break;
            case ContentDialogResult.Secondary:
                DeleteAccountGoogle(viewModel, user);
                break;
        }
    }

    private async void UnlinkGoogle_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is AccountSettingsPageViewModel viewModel
            && viewModel.User.Value is User user)
        {
            var dialog = new ContentDialog
            {
                Title = S.AccountSettingsPage.UnlinkFromGoogle,
                Content = S.AccountSettingsPage.UnlinkFromGoogleBody,
                CloseButtonText = S.Common.No,
                PrimaryButtonText = S.Common.Yes,
            };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                viewModel.UnlinkFromGoogle.Execute(user);
            }
        }
    }

    private async void LinkGoogle_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is AccountSettingsPageViewModel viewModel
            && viewModel.User.Value is User user)
        {
            var cts = new CancellationTokenSource();
            var dialog = new ContentDialog
            {
                Content = "Open browser to sign in with your provider",
                Title = "Sign in with your provider",
                PrimaryButtonText = "Close"
            };

            _ = dialog.ShowAsync().ContinueWith(t =>
            {
                if (t.Result == ContentDialogResult.Primary)
                    cts.Cancel();
            });

            await user.LinkWithRedirectAsync(FirebaseProviderType.Google, cts.Token);

            dialog.Hide(ContentDialogResult.None);
        }
    }
}
