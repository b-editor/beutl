
using Reactive.Bindings;

namespace BEditor.ViewModels.MessageContent
{
    public class PluginCheckViewModel
    {
        public ReactivePropertySlim<string> Name { get; } = new();
        public ReactivePropertySlim<bool> IsEnabled { get; } = new();
    }
}