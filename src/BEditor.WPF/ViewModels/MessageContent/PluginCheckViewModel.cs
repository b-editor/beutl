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
        public ReactiveProperty<string> Name { get; } = new();
        public ReactiveProperty<bool> IsEnabled { get; } = new();
    }
}
