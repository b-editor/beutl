
using System;

using BEditor.Models;
using BEditor.Packaging;

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
                IsLoading.Value = true;
                var (response, user) = await _provider.SignupAsync(Email.Value, Password.Value);
                IsLoading.Value = false;
                if (user is not null && response.Complete)
                {
                    AppModel.Current.User = user;
                    await SuccessSignup.ExecuteAsync();
                }
                else
                {
                    Message.Value = response.Message;
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