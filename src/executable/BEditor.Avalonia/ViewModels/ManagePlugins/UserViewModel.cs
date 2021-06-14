using System;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;

using BEditor.Packaging;
using BEditor.Properties;

using Reactive.Bindings;

namespace BEditor.ViewModels.ManagePlugins
{
    public class UserViewModel
    {
        public UserViewModel(AuthenticationLink auth)
        {
            Auth = auth;

            UserName.Value = auth.User!.DisplayName;
            Email.Value = auth.User!.Email;

            CanEdit.Where(i => !i)
                .Subscribe(async _ =>
                {
                    var nameIsChanged = UserName.Value != Auth.User?.DisplayName;
                    var mailIsChanged = Email.Value != Auth.User!.Email;
                    if (!nameIsChanged && !mailIsChanged) return;

                    var (oldName, oldMail) = (auth.User!.DisplayName, auth.User!.Email);
                    try
                    {
                        if (Auth.IsExpired()) await Auth.RefreshAuthAsync();

                        IsLoading.Value = true;
                        if (nameIsChanged)
                        {
                            await auth.UpdateProfileAsync(UserName.Value);
                        }
                        if (mailIsChanged)
                        {
                            await auth.ChangeUserEmailAsync(Email.Value);
                        }

                        IsLoading.Value = false;
                        EditMessage.Value = Strings.ChangeSucceeded;
                        await Task.Delay(5000);
                        EditMessage.Value = string.Empty;
                    }
                    catch
                    {
                        UserName.Value = oldName;
                        Email.Value = oldMail;
                        IsLoading.Value = false;

                        EditMessage.Value = Strings.ChangeFailed;
                        await Task.Delay(5000);
                        EditMessage.Value = string.Empty;
                    }
                });

            UpdatePassword.Subscribe(async () =>
            {
                if (NewPassword.Value != ConfirmNewPassword.Value)
                {
                    ChangePasswordMessage.Value = Strings.PasswordAreNotMatching;
                }
                else
                {
                    try
                    {
                        IsLoading.Value = true;
                        await Auth.ChangeUserPasswordAsync(NewPassword.Value);
                        ChangePasswordMessage.Value = Strings.ChangeSucceeded;
                    }
                    catch
                    {
                        ChangePasswordMessage.Value = Strings.ChangeFailed;
                    }
                    finally
                    {
                        IsLoading.Value = false;
                    }
                }
            });
        }

        public ReactivePropertySlim<bool> IsLoading { get; } = new(false);

        public ReactivePropertySlim<string> UserName { get; } = new(string.Empty);

        public ReactivePropertySlim<string> Email { get; } = new(string.Empty);

        public ReactivePropertySlim<bool> CanEdit { get; } = new(false);

        public ReactivePropertySlim<string> EditMessage { get; } = new(string.Empty);

        // Change passwrod
        public ReactivePropertySlim<string> NewPassword { get; } = new(string.Empty);

        public ReactivePropertySlim<string> ConfirmNewPassword { get; } = new(string.Empty);

        public ReactiveCommand UpdatePassword { get; } = new();

        public ReactivePropertySlim<string> ChangePasswordMessage { get; } = new(string.Empty);

        public AuthenticationLink Auth { get; }
    }
}