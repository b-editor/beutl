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
            switch (viewModel.SignInWithGoogle.Value)
            {
                case true:
                    DeleteAccountGoogle(viewModel, user);
                    break;
                default:
                    DeleteAccountEmail(viewModel, user);
                    break;
            }
        }
    }

    private async void SignInWithGoogleClick(object? sender, RoutedEventArgs e)
    {
        var flow = new InternalFirebaseUIFlow(this.FindLogicalAncestorOfType<Window>());

        try
        {
            UserCredential userCredential = await FirebaseUI.Instance.SignInAsync(flow, FirebaseProviderType.Google);
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

        if (await showDialogTask == ContentDialogResult.Primary)
        {
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
        }
    }

    private async void DeleteAccountEmail(AccountSettingsPageViewModel viewModel, User user)
    {
        dialog1Text.Text = string.Format(S.AccountSettingsPage.Dialog1.Content, user.Info.Email);

    Label1:
        if (await dialog1.ShowAsync() == ContentDialogResult.Primary)
        {
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
        }
    }

    private sealed class InternalFirebaseUIFlow : IFirebaseUIFlow
    {
        private readonly Window _window;

        public InternalFirebaseUIFlow(Window window)
        {
            _window = window;
        }

        Task<string> IFirebaseUIFlow.GetRedirectResponseUriAsync(FirebaseProviderType provider, string uri)
        {
            string redirectUri = FirebaseUI.Instance.Config.RedirectUri;
            return WebAuthenticationBroker.AuthenticateAsync(_window, provider, uri, redirectUri);
        }
        Task<string> IFirebaseUIFlow.PromptForEmailAsync(string error) => throw new NotImplementedException();
        Task<EmailUser> IFirebaseUIFlow.PromptForEmailPasswordNameAsync(string email, string error) => throw new NotImplementedException();
        Task<EmailPasswordResult> IFirebaseUIFlow.PromptForPasswordAsync(string email, bool oauthEmailAttempt, string error) => throw new NotImplementedException();
        Task<object> IFirebaseUIFlow.PromptForPasswordResetAsync(string email, string error) => throw new NotImplementedException();
        void IFirebaseUIFlow.Reset()
        {
        }
        Task<bool> IFirebaseUIFlow.ShowEmailProviderConflictAsync(string email, FirebaseProviderType providerType) => throw new NotImplementedException();
        Task IFirebaseUIFlow.ShowPasswordResetConfirmationAsync(string email) => throw new NotImplementedException();
    }
}
