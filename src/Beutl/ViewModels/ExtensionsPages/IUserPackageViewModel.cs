using Reactive.Bindings;

namespace Beutl.ViewModels.ExtensionsPages;

public interface IUserPackageViewModel : IDisposable
{
    string Name { get; }

    IReadOnlyReactiveProperty<string?> DisplayName { get; }

    IReadOnlyReactiveProperty<string?> LogoUrl { get; }

    string Publisher { get; }

    bool IsRemote { get; }

    ReadOnlyReactivePropertySlim<bool> IsInstallButtonVisible { get; }

    ReadOnlyReactivePropertySlim<bool> IsUninstallButtonVisible { get; }

    IReadOnlyReactiveProperty<bool> IsUpdateButtonVisible { get; }

    ReadOnlyReactivePropertySlim<bool> CanCancel { get; }

    AsyncReactiveCommand Install { get; }

    AsyncReactiveCommand Update { get; }

    AsyncReactiveCommand Uninstall { get; }

    AsyncReactiveCommand Cancel { get; }

    ReactivePropertySlim<bool> IsBusy { get; }
}
