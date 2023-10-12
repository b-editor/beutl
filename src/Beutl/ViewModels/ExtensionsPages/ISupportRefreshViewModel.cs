using Reactive.Bindings;

namespace Beutl.ViewModels.ExtensionsPages;

public interface ISupportRefreshViewModel
{
    AsyncReactiveCommand Refresh { get; }
}
