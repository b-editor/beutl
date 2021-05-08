
using Reactive.Bindings;

namespace BEditor.ViewModels
{
    public class SplashWindowViewModel
    {
        public ReactivePropertySlim<string> Status { get; } = new();
    }
}