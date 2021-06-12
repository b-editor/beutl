using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Data.Property;
using BEditor.Packaging;
using BEditor.Properties;

using Microsoft.Extensions.DependencyInjection;

using Reactive.Bindings;

namespace BEditor.ViewModels.ManagePlugins
{
    public class UserViewModel
    {
        private readonly IAuthenticationProvider _provider;

        public UserViewModel(User user, IServiceProvider service)
        {
            _provider = service.GetRequiredService<IAuthenticationProvider>();
            User = user;
            UserName.Value = user.UserName;
            Email.Value = user.Email;

            CanEdit.Where(i => !i)
                .Subscribe(async _ =>
                {
                    if (UserName.Value != User.UserName || Email.Value != User.Email)
                    {
                        var (oldName, oldMail) = (user.UserName, user.Email);
                        IsLoading.Value = true;
                        user.UserName = UserName.Value;
                        user.Email = Email.Value;

                        var response = await _provider.UpdateAsync(user);
                        IsLoading.Value = false;
                        if (response.Complete)
                        {
                            EditMessage.Value = Strings.ChangeSucceeded;
                            await Task.Delay(5000);
                            EditMessage.Value = string.Empty;
                        }
                        else
                        {
                            UserName.Value = oldName;
                            Email.Value = oldMail;
                            user.UserName = UserName.Value;
                            user.Email = Email.Value;

                            EditMessage.Value = Strings.ChangeFailed;
                            await Task.Delay(5000);
                            EditMessage.Value = string.Empty;
                        }
                    }
                });
        }

        public ReactivePropertySlim<string> UserName { get; } = new(string.Empty);

        public ReactivePropertySlim<string> Email { get; } = new(string.Empty);

        public ReactivePropertySlim<bool> CanEdit { get; } = new(false);

        public ReactivePropertySlim<bool> IsLoading { get; } = new(false);

        public ReactivePropertySlim<string> EditMessage { get; } = new(string.Empty);

        public User User { get; }
    }
}
