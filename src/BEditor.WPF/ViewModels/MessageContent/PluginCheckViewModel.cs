using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Reactive.Bindings;

namespace BEditor.ViewModels.MessageContent
{
    public class PluginCheckViewModel
    {
        public ReactivePropertySlim<string> Name { get; } = new();
        public ReactivePropertySlim<bool> IsEnabled { get; } = new();
    }
}
