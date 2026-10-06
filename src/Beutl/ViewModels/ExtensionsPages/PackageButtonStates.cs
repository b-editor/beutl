using Beutl.Api.Objects;
using Beutl.Api.Services;
using NuGet.Packaging.Core;
using Reactive.Bindings;

namespace Beutl.ViewModels.ExtensionsPages;

// The cancel and install/update/uninstall button states of a package row in the library.
internal static class PackageButtonStates
{
    public static (
        ReadOnlyReactivePropertySlim<bool> CanCancel,
        ReadOnlyReactivePropertySlim<bool> IsInstallButtonVisible,
        ReadOnlyReactivePropertySlim<bool> IsUpdateButtonVisible,
        ReadOnlyReactivePropertySlim<bool> IsUninstallButtonVisible) Create(
        PackageOperationHandler handler,
        string packageName,
        ReactivePropertySlim<bool> isBusy,
        ReactivePropertySlim<Release?> latestRelease,
        CompositeDisposable disposables)
    {
        IObservable<PackageChangesQueue.EventType> observable = handler.Queue.GetObservable(packageName);
        ReadOnlyReactivePropertySlim<bool> canCancel = observable.Select(x => x != PackageChangesQueue.EventType.None)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(disposables);

        IObservable<bool> installed = handler.InstalledPackageRepository.GetObservable(packageName);
        IObservable<bool> notBusy = isBusy.Not();
        ReadOnlyReactivePropertySlim<bool> isInstallButtonVisible = installed
            .AnyTrue(canCancel)
            .Not()
            .AreTrue(notBusy)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(disposables);

        IObservable<PackageIdentity?> installedPackage = handler.InstalledPackageRepository.GetPackageObservable(packageName);
        ReadOnlyReactivePropertySlim<bool> isUpdateButtonVisible = latestRelease.CombineLatest(installedPackage)
            .Select(x => PackageUpdateAvailability.IsAvailable(x.First?.Version.Value, x.Second))
            .AreTrue(canCancel.Not(), notBusy)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(disposables);

        ReadOnlyReactivePropertySlim<bool> isUninstallButtonVisible = installed
            .AreTrue(canCancel.Not(), isUpdateButtonVisible.Not(), notBusy)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(disposables);

        return (canCancel, isInstallButtonVisible, isUpdateButtonVisible, isUninstallButtonVisible);
    }
}
