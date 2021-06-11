using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Reactive.Bindings;

namespace BEditor.ViewModels.ManagePlugins
{
    public class SigninViewModel
    {
        public ReactiveProperty<string> Email { get; } = new();

        public ReactiveProperty<string> Password { get; } = new();

        public ReactiveCommand Signin { get; } = new();

        public ReactivePropertySlim<string> Message { get; } = new(string.Empty);
    }
}
