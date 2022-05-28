using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;

using BeUtl.Controls;
using BeUtl.Language;
using BeUtl.ViewModels.SettingsPages;

using Firebase.Auth;
using Firebase.Auth.UI;

using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Controls.Primitives;

using S = BeUtl.Language.StringResources;

namespace BeUtl.Pages.SettingsPages;

public sealed partial class AccountSettingsPage : UserControl
{
    public AccountSettingsPage()
    {
        InitializeComponent();
    }

    private void UploadProfileImage_Click(object? sender, RoutedEventArgs e)
    {
    }

    private async void DeleteAccount_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is AccountSettingsPageViewModel viewModel
            && viewModel.User.Value is User user)
        {
            var textBox = new TextBox
            {
                Classes = { "revealPasswordButton" },
                PasswordChar = '*'
            };
            var errorText = new TextBlock
            {
                Classes = { "ErrorTextBlockStyle" },
                Opacity = 0,
            };
            var dialog = new ContentDialog
            {
                Title = S.AccountSettingsPage.Dialog1.Title,
                DefaultButton = ContentDialogButton.Close,
                CloseButtonText = S.AccountSettingsPage.Dialog1.No,
                PrimaryButtonText = S.AccountSettingsPage.Dialog1.Yes,
                Content = new StackPanel
                {
                    Children =
                    {
                        new TextBlock
                        {
                            Text = string.Format(S.AccountSettingsPage.Dialog1.Content, user.Info.Email),
                        },
                        new TextBlock
                        {
                            Margin = new Thickness(0, 12, 0, 8),
                            Text = S.Common.Password,
                            Classes = { "CaptionTextBlockStyle" }
                        },
                        textBox,
                        errorText
                    }
                }
            };

        Label1:
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                try
                {
                    UserCredential credential = await FirebaseUI.Instance.Client.SignInWithEmailAndPasswordAsync(user.Info.Email, textBox.Text);
                    viewModel.DeleteAccount.Execute(credential.User);
                }
                catch (FirebaseAuthException ex)
                {
                    errorText.Text = FirebaseErrorLookup.LookupError(ex);
                    errorText.Opacity = 1;
                    goto Label1;
                }
            }
        }
    }
}
