
using System;

using BEditor.Models;
using BEditor.Packaging;
using BEditor.Properties;

using Microsoft.Extensions.DependencyInjection;

using Reactive.Bindings;

namespace BEditor.ViewModels.ManagePlugins
{
    public class SignupViewModel
    {
        private readonly IAuthenticationProvider _provider;

        public SignupViewModel(IServiceProvider services)
        {
            _provider = services.GetRequiredService<IAuthenticationProvider>();

            Signup.Subscribe(async () =>
            {
                try
                {
                    IsLoading.Value = true;
                    AppModel.Current.User = (AuthenticationLink?)await _provider.CreateUserAsync(Email.Value, Password.Value);
                    await SuccessSignup.ExecuteAsync();
                }
                catch
                {
                    Message.Value = Message.Value = string.Format(Strings.FailedTo, Strings.Signup); ;
                }
                finally
                {
                    IsLoading.Value = false;
                }
            });
        }

        public ReactivePropertySlim<bool> IsLoading { get; } = new(false);

        public ReactiveProperty<string> Email { get; } = new(string.Empty);

        public ReactiveProperty<string> Password { get; } = new(string.Empty);

        public AsyncReactiveCommand Signup { get; } = new();

        public AsyncReactiveCommand SuccessSignup { get; } = new();

        public ReactivePropertySlim<string> Message { get; } = new(string.Empty);
    }
}