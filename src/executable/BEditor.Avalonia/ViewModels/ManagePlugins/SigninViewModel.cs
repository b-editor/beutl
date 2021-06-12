using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Models;
using BEditor.Packaging;
using BEditor.Properties;

using Microsoft.Extensions.DependencyInjection;

using Reactive.Bindings;

namespace BEditor.ViewModels.ManagePlugins
{
    public class SigninViewModel
    {
        private readonly IAuthenticationProvider _provider;

        public SigninViewModel(IServiceProvider service)
        {
            _provider = service.GetRequiredService<IAuthenticationProvider>();

            Signin.Subscribe(async () =>
            {
                try
                {
                    IsLoading.Value = true;
                    AppModel.Current.User = (AuthenticationLink?)await _provider.SignInAsync(Email.Value, Password.Value);
                    await SuccessSignin.ExecuteAsync();
                }
                catch
                {
                    Message.Value = string.Format(Strings.FailedTo, Strings.Signin);
                }
                finally
                {
                    IsLoading.Value = false;
                }
            });
        }

        public ReactivePropertySlim<bool> IsLoading { get; } = new(false);

        public ReactiveProperty<string> Email { get; } = new();

        public ReactiveProperty<string> Password { get; } = new();

        public AsyncReactiveCommand Signin { get; } = new();

        public AsyncReactiveCommand SuccessSignin { get; } = new();

        public ReactivePropertySlim<string> Message { get; } = new(string.Empty);
    }
}