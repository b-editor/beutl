using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Models;
using BEditor.Packaging;

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
                IsLoading.Value = true;
                var (response, user) = await _provider.SigninAsync(Email.Value, Password.Value);
                IsLoading.Value = false;
                if (user is not null && response.Complete)
                {
                    AppModel.Current.User = user;
                    await SuccessSignin.ExecuteAsync();
                }
                else
                {
                    Message.Value = response.Message;
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