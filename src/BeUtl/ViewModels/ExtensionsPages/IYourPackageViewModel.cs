using Reactive.Bindings;

namespace BeUtl.ViewModels.ExtensionsPages;

public interface IYourPackageViewModel : IDisposable
{
    string Name { get; }

    IReadOnlyReactiveProperty<string> DisplayName { get; }

    IReadOnlyReactiveProperty<string> LogoUrl { get; }

    string Publisher { get; }

    ReadOnlyReactivePropertySlim<bool> IsInstallButtonVisible { get; }

    ReadOnlyReactivePropertySlim<bool> IsUninstallButtonVisible { get; }

    ReactivePropertySlim<bool> IsUpdateButtonVisible { get; }

    ReadOnlyReactivePropertySlim<bool> CanCancel { get; }

    AsyncReactiveCommand Install { get; }

    AsyncReactiveCommand Update { get; }

    ReactiveCommand Uninstall { get; }

    ReactiveCommand Cancel { get; }

    ReactivePropertySlim<bool> IsBusy { get; }
}
